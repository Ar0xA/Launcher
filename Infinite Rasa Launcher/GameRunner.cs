using System;
using System.Diagnostics;
using System.IO;

namespace Infinite_Rasa_Launcher
{
    /// <summary>Starts the game client according to the configured LaunchMode.</summary>
    public static class GameRunner
    {
        public static void Start(LaunchSettings settings, String exeName, String arguments)
        {
            String gameDir = settings.EffectiveGameDirectory;
            String exePath = Path.Combine(gameDir, exeName);
            ProcessStartInfo info;

            switch (settings.Mode)
            {
                case LaunchMode.Lutris:
                    Lutris.UpdateGameConfig(settings.LutrisConfigFile, exePath, arguments, gameDir);
                    Lutris.Run(settings.LutrisCommand, settings.LutrisGameId);
                    return;

                case LaunchMode.Wine:
                    info = new ProcessStartInfo(settings.WinePath, Quote(exePath) + " " + arguments);
                    info.UseShellExecute = false;
                    if (!String.IsNullOrEmpty(settings.WinePrefix))
                    {
                        info.EnvironmentVariables["WINEPREFIX"] = settings.WinePrefix;
                    }
                    break;

                default:
                    info = new ProcessStartInfo(exePath, arguments);
                    info.UseShellExecute = false;
                    break;
            }

            info.WorkingDirectory = gameDir;
            Process.Start(info);
        }

        private static String Quote(String value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
