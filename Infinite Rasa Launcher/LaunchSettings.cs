using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Infinite_Rasa_Launcher
{
    public enum LaunchMode
    {
        Direct, // start tabula_rasa.exe directly (Windows, or a Wine-registered binfmt)
        Wine,   // start it through a wine binary, optionally with a WINEPREFIX
        Lutris  // start it through an installed Lutris game entry
    }

    /// <summary>
    /// User-editable launch configuration, stored as key=value lines in
    /// %APPDATA%\InfiniteRasaLauncher\settings.ini (Windows) or
    /// ~/.config/InfiniteRasaLauncher/settings.ini (Mono on Linux).
    /// </summary>
    public class LaunchSettings
    {
        public LaunchMode Mode          { get; set; }
        public String GameDirectory     { get; set; }
        public String GameExe           { get; set; }
        public String GameArguments     { get; set; }
        public String WorkingDirectory  { get; set; }
        public String ServerAddress     { get; set; }
        public String WinePath          { get; set; }
        public String WinePrefix        { get; set; }
        public String LutrisCommand     { get; set; }
        public String LutrisGameId      { get; set; }
        public String LutrisGameName    { get; set; }
        public String LutrisConfigFile  { get; set; }

        public static String SettingsFile
        {
            get
            {
                String appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(appData, "InfiniteRasaLauncher"), "settings.ini");
            }
        }

        public static bool IsUnix
        {
            get { return Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX; }
        }

        public const String DefaultGameExe = "tabula_rasa.exe";
        public const String DefaultServerAddress = "127.0.0.1:2106";
        public const String DefaultGameArguments = "/NoPatch /AuthServer={host}:{port}";

        public LaunchSettings()
        {
            Mode = IsUnix ? LaunchMode.Wine : LaunchMode.Direct;
            GameDirectory = "";
            GameExe = DefaultGameExe;
            GameArguments = DefaultGameArguments;
            WorkingDirectory = "";
            ServerAddress = DefaultServerAddress;
            WinePath = "wine";
            WinePrefix = "";
            LutrisCommand = Lutris.DetectCommand();
            LutrisGameId = "";
            LutrisGameName = "";
            LutrisConfigFile = "";
        }

        public static LaunchSettings Load()
        {
            LaunchSettings settings = new LaunchSettings();
            if (!File.Exists(SettingsFile)) { return settings; }

            foreach (String line in File.ReadAllLines(SettingsFile))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith("#")) { continue; }
                String key = line.Substring(0, eq).Trim();
                String value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "Mode":
                        try
                        {
                            LaunchMode mode = (LaunchMode)Enum.Parse(typeof(LaunchMode), value, true);
                            if (Enum.IsDefined(typeof(LaunchMode), mode)) { settings.Mode = mode; }
                        }
                        catch (ArgumentException) { }
                        break;
                    case "GameDirectory":    settings.GameDirectory = value; break;
                    case "GameExe":          if (value != "") { settings.GameExe = value; } break;
                    case "GameArguments":    settings.GameArguments = value; break;
                    case "WorkingDirectory": settings.WorkingDirectory = value; break;
                    case "ServerAddress":    if (value != "") { settings.ServerAddress = value; } break;
                    case "WinePath":         settings.WinePath = value; break;
                    case "WinePrefix":       settings.WinePrefix = value; break;
                    case "LutrisCommand":    if (value != "") { settings.LutrisCommand = value; } break;
                    case "LutrisGameId":     settings.LutrisGameId = value; break;
                    case "LutrisGameName":   settings.LutrisGameName = value; break;
                    case "LutrisConfigFile": settings.LutrisConfigFile = value; break;
                }
            }
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Infinite Rasa Launcher settings");
            sb.AppendLine("Mode=" + Mode);
            sb.AppendLine("GameDirectory=" + GameDirectory);
            sb.AppendLine("GameExe=" + GameExe);
            sb.AppendLine("GameArguments=" + GameArguments);
            sb.AppendLine("WorkingDirectory=" + WorkingDirectory);
            sb.AppendLine("ServerAddress=" + ServerAddress);
            sb.AppendLine("WinePath=" + WinePath);
            sb.AppendLine("WinePrefix=" + WinePrefix);
            sb.AppendLine("LutrisCommand=" + LutrisCommand);
            sb.AppendLine("LutrisGameId=" + LutrisGameId);
            sb.AppendLine("LutrisGameName=" + LutrisGameName);
            sb.AppendLine("LutrisConfigFile=" + LutrisConfigFile);
            File.WriteAllText(SettingsFile, sb.ToString());
        }

        /// <summary>
        /// Directory the game lives in (and that the patcher writes to).
        /// Falls back to the current directory, which was the original behaviour.
        /// </summary>
        public String EffectiveGameDirectory
        {
            get { return String.IsNullOrEmpty(GameDirectory) ? Directory.GetCurrentDirectory() : Path.GetFullPath(GameDirectory); }
        }

        /// <summary>Full path of the game executable; GameExe may be absolute or relative to the game directory.</summary>
        public String EffectiveExePath
        {
            get { return Path.Combine(EffectiveGameDirectory, GameExe); }
        }

        /// <summary>Directory the game is started in; empty WorkingDirectory = the game directory.</summary>
        public String EffectiveWorkingDirectory
        {
            get
            {
                if (String.IsNullOrEmpty(WorkingDirectory)) { return EffectiveGameDirectory; }
                return Path.Combine(EffectiveGameDirectory, WorkingDirectory);
            }
        }

        /// <summary>Game arguments with {host}, {port} and {server} (host:port) replaced.</summary>
        public String ExpandArguments(String host, String port)
        {
            return GameArguments.Replace("{server}", host + ":" + port)
                                .Replace("{host}", host)
                                .Replace("{port}", port);
        }

        /// <summary>Splits ServerAddress ("host:port", port defaults to 2106). Returns false if it is invalid.</summary>
        public bool TryGetServer(out String host, out Int16 port)
        {
            String address = (ServerAddress ?? "").Trim();
            int colon = address.LastIndexOf(':');
            host = colon < 0 ? address : address.Substring(0, colon).Trim();
            port = 2106;
            if (colon >= 0 && !Int16.TryParse(address.Substring(colon + 1).Trim(), out port)) { return false; }
            return host != "" && port > 0;
        }

        /// <summary>Returns a list of problems that prevent launching; empty if the settings are usable.</summary>
        public List<String> Validate()
        {
            List<String> problems = new List<String>();

            if (!String.IsNullOrEmpty(GameDirectory) && !Directory.Exists(GameDirectory))
            {
                problems.Add("Game directory does not exist: " + GameDirectory);
            }
            else if (String.IsNullOrEmpty(GameExe))
            {
                problems.Add("Game executable is not set.");
            }
            // a missing exe is not checked here: the patcher may still download it
            else if (!Directory.Exists(EffectiveWorkingDirectory))
            {
                problems.Add("Working directory does not exist: " + EffectiveWorkingDirectory);
            }

            String host;
            Int16 port;
            if (!TryGetServer(out host, out port))
            {
                problems.Add("Server must be host:port with a port from 1 to 32767: " + ServerAddress);
            }

            if (Mode == LaunchMode.Wine && String.IsNullOrEmpty(WinePath))
            {
                problems.Add("Wine binary is not set.");
            }
            if (Mode == LaunchMode.Wine && !String.IsNullOrEmpty(WinePrefix) && !Directory.Exists(WinePrefix))
            {
                problems.Add("Wine prefix does not exist: " + WinePrefix);
            }

            if (Mode == LaunchMode.Lutris)
            {
                if (String.IsNullOrEmpty(LutrisCommand)) { problems.Add("Lutris command is not set."); }
                if (String.IsNullOrEmpty(LutrisGameId))  { problems.Add("No Lutris game selected."); }
                if (String.IsNullOrEmpty(LutrisConfigFile) || !File.Exists(LutrisConfigFile))
                {
                    problems.Add("Lutris game config file (.yml) not found: " + LutrisConfigFile);
                }
            }
            return problems;
        }
    }
}
