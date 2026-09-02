using System;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace NightLights
{
    /// <summary>
    /// All user-configurable settings, persisted as JSON under %AppData%\NightLights.
    /// Kept deliberately tiny: no database, no installer, just a text file.
    /// Public (not internal): SettingsForm's public constructor and Result property
    /// both take/return an AppSettings, and a public member can't expose a less
    /// accessible type - this class has to be at least as public as SettingsForm.
    /// </summary>
    public sealed class AppSettings
    {
        public static readonly string AppDataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NightLights");

        private static readonly string SettingsPath = Path.Combine(AppDataFolder, "settings.json");

        // Warsaw is used as the out-of-the-box default location (matches the machine's timezone);
        // change it from the tray Settings dialog to your real coordinates for accurate sunset/sunrise.
        public double Latitude { get; set; } = 52.2297;
        public double Longitude { get; set; } = 21.0122;

        public bool ControlFuryDram { get; set; } = true;
        public bool ControlMysticLight { get; set; } = true;

        public bool RunAtStartup { get; set; } = false;

        // How often (seconds) we re-check whether it's day or night.
        public int PollIntervalSeconds { get; set; } = 60;

        // Manual override: null = follow the sun automatically.
        public bool? ManualNightOverride { get; set; } = null;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("AppSettings.Load failed, using defaults: " + ex);
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(SettingsPath, serializer.Serialize(this));
            }
            catch (Exception ex)
            {
                Logger.Log("AppSettings.Save failed: " + ex);
            }
        }

        // --- Run-at-Windows-startup, via the per-user Run key (no admin rights needed) ---

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "NightLights";

        public static void ApplyRunAtStartup(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (key == null) return;

                    if (enabled)
                    {
                        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunValueName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                        {
                            key.DeleteValue(RunValueName, throwOnMissingValue: false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ApplyRunAtStartup failed: " + ex);
            }
        }
    }
}
