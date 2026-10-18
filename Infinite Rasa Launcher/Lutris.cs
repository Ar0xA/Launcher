using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Infinite_Rasa_Launcher
{
    [DataContract]
    public class LutrisGame
    {
        [DataMember(Name = "id")]        public int    Id        { get; set; }
        [DataMember(Name = "slug")]      public String Slug      { get; set; }
        [DataMember(Name = "name")]      public String Name      { get; set; }
        [DataMember(Name = "runner")]    public String Runner    { get; set; }
        [DataMember(Name = "directory")] public String Directory { get; set; }

        public override String ToString()
        {
            return Name + "  [" + Slug + ", id " + Id + ", " + Runner + "]";
        }
    }

    /// <summary>
    /// Helpers for running the game through a Lutris (native or Flatpak) game entry.
    ///
    /// Lutris cannot pass extra arguments through its lutris:rungameid/ URI, so before
    /// each launch the game's .yml config is rewritten to point at tabula_rasa.exe with
    /// the /AuthServer argument of the selected server. The original .yml is kept once
    /// as &lt;file&gt;.irl-backup.
    /// </summary>
    public static class Lutris
    {
        public const String FlatpakId = "net.lutris.Lutris";
        public const String BackupSuffix = ".irl-backup";

        private static String Home
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        }

        /// <summary>Returns "lutris" if it is on PATH, else the Flatpak command if installed, else "lutris".</summary>
        public static String DetectCommand()
        {
            if (!LaunchSettings.IsUnix) { return "lutris"; }

            String path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (String dir in path.Split(':'))
            {
                if (dir != "" && File.Exists(Path.Combine(dir, "lutris"))) { return "lutris"; }
            }

            if (Directory.Exists("/var/lib/flatpak/app/" + FlatpakId) ||
                Directory.Exists(Path.Combine(Home, ".local/share/flatpak/app/" + FlatpakId)))
            {
                return "flatpak run " + FlatpakId;
            }
            return "lutris";
        }

        /// <summary>Directories where Lutris keeps per-game .yml configs (native and Flatpak, old and new layouts).</summary>
        public static List<String> ConfigDirectories()
        {
            String[] candidates = new String[]
            {
                Path.Combine(Home, ".var/app/" + FlatpakId + "/data/lutris/games"),
                Path.Combine(Home, ".var/app/" + FlatpakId + "/config/lutris/games"),
                Path.Combine(Home, ".local/share/lutris/games"),
                Path.Combine(Home, ".config/lutris/games"),
            };
            List<String> dirs = new List<String>();
            foreach (String dir in candidates)
            {
                if (Directory.Exists(dir)) { dirs.Add(dir); }
            }
            return dirs;
        }

        /// <summary>
        /// Config files that belong to a game slug (named "&lt;slug&gt;-&lt;timestamp&gt;.yml"), newest first.
        /// Configs inside the directory that matches the Lutris command (Flatpak or native) come first.
        /// </summary>
        public static List<String> FindConfigFiles(String slug, String lutrisCommand)
        {
            bool flatpak = lutrisCommand != null && lutrisCommand.Contains(FlatpakId);
            // installer-created configs may carry the installer slug: "cps-baofeng-setup-1761325760.yml"
            Regex pattern = new Regex("^" + Regex.Escape(slug) + "(-[a-z0-9-]*)?-\\d+\\.yml$");
            Regex exact = new Regex("^" + Regex.Escape(slug) + "-\\d+\\.yml$");
            List<FileInfo> found = new List<FileInfo>();

            foreach (String dir in ConfigDirectories())
            {
                foreach (String file in Directory.GetFiles(dir, "*.yml"))
                {
                    if (pattern.IsMatch(Path.GetFileName(file))) { found.Add(new FileInfo(file)); }
                }
            }

            found.Sort(delegate(FileInfo a, FileInfo b)
            {
                bool aMatch = a.FullName.Contains(FlatpakId) == flatpak;
                bool bMatch = b.FullName.Contains(FlatpakId) == flatpak;
                if (aMatch != bMatch) { return aMatch ? -1 : 1; }
                bool aExact = exact.IsMatch(a.Name);
                bool bExact = exact.IsMatch(b.Name);
                if (aExact != bExact) { return aExact ? -1 : 1; }
                return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
            });

            List<String> result = new List<String>();
            foreach (FileInfo f in found) { result.Add(f.FullName); }
            return result;
        }

        /// <summary>Runs "&lt;lutris&gt; --list-games --installed --json" and parses the result.</summary>
        public static List<LutrisGame> ListGames(String lutrisCommand)
        {
            String output = RunAndCapture(lutrisCommand, "--list-games --installed --json", 60000);
            int start = output.IndexOf('[');
            int end = output.LastIndexOf(']');
            if (start < 0 || end < start)
            {
                throw new InvalidOperationException("Lutris returned no game list. Output:\n" + output);
            }

            byte[] json = Encoding.UTF8.GetBytes(output.Substring(start, end - start + 1));
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<LutrisGame>));
            using (MemoryStream ms = new MemoryStream(json))
            {
                return (List<LutrisGame>)serializer.ReadObject(ms);
            }
        }

        /// <summary>Starts the Lutris game entry (lutris:rungameid/&lt;id&gt;).</summary>
        public static Process Run(String lutrisCommand, String gameId)
        {
            String file, args;
            SplitCommand(lutrisCommand, out file, out args);

            ProcessStartInfo info = new ProcessStartInfo(file, (args + " lutris:rungameid/" + gameId).Trim());
            info.UseShellExecute = false;
            return Process.Start(info);
        }

        /// <summary>
        /// Rewrites exe, args and working_dir in the top-level "game:" section of a Lutris .yml,
        /// leaving every other line untouched. Keeps a one-time backup of the original file.
        /// </summary>
        public static void UpdateGameConfig(String ymlPath, String exe, String arguments, String workingDir)
        {
            String backup = ymlPath + BackupSuffix;
            if (!File.Exists(backup)) { File.Copy(ymlPath, backup); }

            List<String> lines = new List<String>(File.ReadAllLines(ymlPath));
            String[] keys = new String[] { "exe", "args", "working_dir" };

            int gameLine = lines.FindIndex(delegate(String l) { return l.TrimEnd() == "game:"; });
            if (gameLine < 0)
            {
                lines.Add("game:");
                gameLine = lines.Count - 1;
            }

            // remove the keys we own (and any continuation lines of multi-line values) from the game block
            int i = gameLine + 1;
            while (i < lines.Count && (lines[i].Trim() == "" || Char.IsWhiteSpace(lines[i][0])))
            {
                String trimmed = lines[i].TrimStart();
                int indent = lines[i].Length - trimmed.Length;
                bool owned = false;
                foreach (String key in keys)
                {
                    if (trimmed.StartsWith(key + ":")) { owned = true; break; }
                }

                if (!owned) { i++; continue; }

                lines.RemoveAt(i);
                while (i < lines.Count && lines[i].Trim() != "" &&
                       lines[i].Length - lines[i].TrimStart().Length > indent)
                {
                    lines.RemoveAt(i);
                }
            }

            lines.Insert(gameLine + 1, "  exe: " + YamlQuote(exe));
            lines.Insert(gameLine + 2, "  args: " + YamlQuote(arguments));
            lines.Insert(gameLine + 3, "  working_dir: " + YamlQuote(workingDir));

            String tmp = ymlPath + ".tmp";
            File.WriteAllText(tmp, String.Join("\n", lines.ToArray()) + "\n");
            File.Copy(tmp, ymlPath, true);
            File.Delete(tmp);
        }

        private static String YamlQuote(String value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>Splits "flatpak run net.lutris.Lutris" into the executable and its leading arguments.</summary>
        public static void SplitCommand(String command, out String file, out String args)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    file = command.Substring(1, close - 1);
                    args = command.Substring(close + 1).Trim();
                    return;
                }
            }
            int space = command.IndexOf(' ');
            file = space < 0 ? command : command.Substring(0, space);
            args = space < 0 ? "" : command.Substring(space + 1).Trim();
        }

        private static String RunAndCapture(String command, String extraArgs, int timeoutMs)
        {
            String file, args;
            SplitCommand(command, out file, out args);

            ProcessStartInfo info = new ProcessStartInfo(file, (args + " " + extraArgs).Trim());
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.CreateNoWindow = true;

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = info;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { stdout.AppendLine(e.Data); } };
                p.ErrorDataReceived  += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { stderr.AppendLine(e.Data); } };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                    throw new TimeoutException("Lutris did not answer within " + (timeoutMs / 1000) + " seconds.");
                }
                p.WaitForExit(); // flush async readers
            }
            return stdout.Length > 0 ? stdout.ToString() : stderr.ToString();
        }
    }
}
