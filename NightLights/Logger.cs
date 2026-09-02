using System;
using System.IO;

namespace NightLights
{
    /// <summary>
    /// Tiny rolling text logger, written to %AppData%\NightLights\NightLights.log.
    /// Every write is best-effort: logging must never crash the app.
    /// </summary>
    internal static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppSettings.AppDataFolder, "NightLights.log");
        private static readonly object Gate = new object();
        private const long MaxBytes = 1024 * 1024; // 1 MB, then we truncate

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppSettings.AppDataFolder);

                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    {
                        File.Delete(LogPath);
                    }

                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
