using System;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NightLights.Tests
{
    internal static class SettingsTests
    {
        public static void Run()
        {
            var serializer = new JavaScriptSerializer();
            var legacy = serializer.Deserialize<AppSettings>("{\"ControlFuryDram\":false,\"Latitude\":50,\"ManualNightOverride\":true}");
            legacy.Normalize();
            TestAssert.True(!legacy.ControlOpenRgb && !legacy.PowerSaverAtNight, "New modules stay opt-in for existing users");
            TestAssert.Equal(NightScheduleMode.FollowSun, legacy.ScheduleMode, "Legacy sun scheduling preserved");
            TestAssert.Equal(true, legacy.ManualNightOverride.Value, "Legacy manual override preserved");
            TestAssert.Equal(6742, legacy.OpenRgbPort, "Default SDK port");
            TestAssert.Equal(1320, legacy.QuietHoursStartMinutes, "Default quiet hours");
            var invalid = new AppSettings { Latitude = double.NaN, Longitude = 999, OpenRgbHost = "http://localhost:6742", OpenRgbPort = -1, PollIntervalSeconds = int.MaxValue, ScheduleMode = (NightScheduleMode)99, QuietHoursStartMinutes = 0, QuietHoursEndMinutes = 0 };
            invalid.Normalize();
            TestAssert.Equal(52.2297, invalid.Latitude, "Invalid latitude repaired");
            TestAssert.Equal(180.0, invalid.Longitude, "Longitude clamped");
            TestAssert.Equal("127.0.0.1", invalid.OpenRgbHost, "Invalid server repaired");
            TestAssert.Equal(3600, invalid.PollIntervalSeconds, "Poll cannot overflow WinForms timer");
            TestAssert.True(AppSettings.IsValidOpenRgbHost("::1"), "IPv6 supported");
            TestAssert.True(!AppSettings.IsValidOpenRgbHost("localhost/path"), "Reject paths in host");

            var configured = new AppSettings { ControlOpenRgb = true, OpenRgbHost = "localhost", OpenRgbPort = 12345, PowerSaverAtNight = true, ScheduleMode = NightScheduleMode.QuietHours, QuietHoursStartMinutes = 1385, QuietHoursEndMinutes = 365, DayProfileBrightness = 37, ManualNightOverride = false };
            var roundTrip = serializer.Deserialize<AppSettings>(serializer.Serialize(configured));
            roundTrip.Normalize();
            TestAssert.Equal(configured.QuietHoursStartMinutes, roundTrip.QuietHoursStartMinutes, "Schedule survives JSON roundtrip");
            TestAssert.True(roundTrip.PowerSaverAtNight && roundTrip.ControlOpenRgb, "Enabled modules survive JSON roundtrip");
            using (var form = new SettingsForm(configured))
            {
                // Save the form in memory only; no tray controller or hardware is instantiated.
                typeof(SettingsForm).GetMethod("BtnOk_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { null, EventArgs.Empty });
                TestAssert.Equal(DialogResult.OK, form.DialogResult, "Valid settings can be saved");
                TestAssert.Equal(serializer.Serialize(configured), serializer.Serialize(form.Result), "Every field survives Settings save");
                TestAssert.True(!ReferenceEquals(configured, form.Result), "Cancel/edit does not mutate active settings object");
            }
        }
    }
}
