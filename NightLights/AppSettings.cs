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

        public bool ControlOpenRgb { get; set; } = false;
        public string OpenRgbHost { get; set; } = "127.0.0.1";
        public int OpenRgbPort { get; set; } = 6742;

        public NightScheduleMode ScheduleMode { get; set; } = NightScheduleMode.FollowSun;
        public int QuietHoursStartMinutes { get; set; } = 22 * 60;
        public int QuietHoursEndMinutes { get; set; } = 7 * 60;
        public bool PowerSaverAtNight { get; set; } = false;

        // Off by default: silencing the whole PC is a bigger behavior change than dimming
        // some RGB, so this one's opt-in. When on, system audio is muted at sunset/"Force
        // night" and unmuted at sunrise/"Force day" - just once per transition (not re-sent
        // every poll like the lighting), so you can still manually unmute at night if you want to.
        public bool SilenceVolumeAtNight { get; set; } = false;

        public bool RunAtStartup { get; set; } = false;

        // How often (seconds) we re-check whether it's day or night.
        public int PollIntervalSeconds { get; set; } = 60;

        // Manual override: null = follow the configured automatic schedule.
        public bool? ManualNightOverride { get; set; } = null;

        // Last brightness (0-100) used by "Set day profile color...". Kingston FURY CTRL's own
        // default is 80; remembered here so the dialog doesn't reset to it every time.
        public int DayProfileBrightness { get; set; } = 80;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
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
                Normalize();
                Directory.CreateDirectory(AppDataFolder);
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(SettingsPath, serializer.Serialize(this));
            }
            catch (Exception ex)
            {
                Logger.Log("AppSettings.Save failed: " + ex);
            }
        }

        public AppSettings Copy() => (AppSettings)MemberwiseClone();

        // Settings may also be edited by hand, or come from an older release.
        public void Normalize()
        {
            if (double.IsNaN(Latitude) || double.IsInfinity(Latitude)) Latitude = 52.2297;
            if (double.IsNaN(Longitude) || double.IsInfinity(Longitude)) Longitude = 21.0122;
            Latitude = Math.Max(-90, Math.Min(90, Latitude));
            Longitude = Math.Max(-180, Math.Min(180, Longitude));
            PollIntervalSeconds = Math.Max(15, Math.Min(3600, PollIntervalSeconds));
            DayProfileBrightness = Math.Max(0, Math.Min(100, DayProfileBrightness));
            if (!Enum.IsDefined(typeof(NightScheduleMode), ScheduleMode)) ScheduleMode = NightScheduleMode.FollowSun;
            QuietHoursStartMinutes = Math.Max(0, Math.Min(1439, QuietHoursStartMinutes));
            QuietHoursEndMinutes = Math.Max(0, Math.Min(1439, QuietHoursEndMinutes));
            if (QuietHoursStartMinutes == QuietHoursEndMinutes)
            {
                QuietHoursStartMinutes = 22 * 60;
                QuietHoursEndMinutes = 7 * 60;
            }
            OpenRgbHost = (OpenRgbHost ?? "").Trim();
            if (!IsValidOpenRgbHost(OpenRgbHost)) OpenRgbHost = "127.0.0.1";
            if (OpenRgbPort < 1 || OpenRgbPort > 65535) OpenRgbPort = 6742;
        }

        internal static bool IsValidOpenRgbHost(string host) =>
            !string.IsNullOrWhiteSpace(host) && host.Length <= 253 &&
            Uri.CheckHostName(host) != UriHostNameType.Unknown;

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
