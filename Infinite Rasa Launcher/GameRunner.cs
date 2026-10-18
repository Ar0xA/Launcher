using System;
using System.Diagnostics;
using System.IO;

namespace Infinite_Rasa_Launcher
{
    /// <summary>Starts the game client according to the configured LaunchMode.</summary>
    public static class GameRunner
    {
        public static void Start(LaunchSettings settings, String host, String port)
        {
            String exePath = settings.EffectiveExePath;
            String workDir = settings.EffectiveWorkingDirectory;
            String arguments = settings.ExpandArguments(host, port);
            ProcessStartInfo info;

            switch (settings.Mode)
            {
                case LaunchMode.Lutris:
                    Lutris.UpdateGameConfig(settings.LutrisConfigFile, exePath, arguments, workDir);
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
                    // ShellExecute on Windows, as before: it shows the UAC prompt for exes that need elevation
                    info.UseShellExecute = !LaunchSettings.IsUnix;
                    break;
            }

            info.WorkingDirectory = workDir;
            Process.Start(info);
        }

        private static String Quote(String value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
