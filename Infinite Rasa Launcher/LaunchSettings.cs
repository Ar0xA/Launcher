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

        public LaunchSettings()
        {
            Mode = IsUnix ? LaunchMode.Wine : LaunchMode.Direct;
            GameDirectory = "";
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
                        try { settings.Mode = (LaunchMode)Enum.Parse(typeof(LaunchMode), value, true); }
                        catch (ArgumentException) { }
                        break;
                    case "GameDirectory":    settings.GameDirectory = value; break;
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
            get { return String.IsNullOrEmpty(GameDirectory) ? Directory.GetCurrentDirectory() : GameDirectory; }
        }

        /// <summary>Returns a list of problems that prevent launching; empty if the settings are usable.</summary>
        public List<String> Validate(String exeName)
        {
            List<String> problems = new List<String>();

            if (!String.IsNullOrEmpty(GameDirectory) && !Directory.Exists(GameDirectory))
            {
                problems.Add("Game directory does not exist: " + GameDirectory);
            }
            else if (!File.Exists(Path.Combine(EffectiveGameDirectory, exeName)))
            {
                problems.Add(exeName + " not found in " + EffectiveGameDirectory);
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
