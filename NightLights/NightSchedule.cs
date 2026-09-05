using System;

namespace NightLights
{
    public enum NightScheduleMode
    {
        FollowSun,
        QuietHours,
        SunOrQuietHours
    }

    internal static class NightSchedule
    {
        public static bool IsNight(AppSettings settings, DateTime localNow)
        {
            if (settings.ManualNightOverride.HasValue) return settings.ManualNightOverride.Value;
            bool quiet = IsWithinQuietHours(localNow.TimeOfDay,
                settings.QuietHoursStartMinutes, settings.QuietHoursEndMinutes);
            if (settings.ScheduleMode == NightScheduleMode.QuietHours) return quiet;
            if (settings.ScheduleMode == NightScheduleMode.SunOrQuietHours && quiet) return true;
            return SunTimes.IsNight(localNow, settings.Latitude, settings.Longitude);
        }

        // Start is inclusive, end is exclusive. Local clock time follows DST changes.
        internal static bool IsWithinQuietHours(TimeSpan time, int startMinutes, int endMinutes)
        {
            if (startMinutes == endMinutes) return false;
            double minutes = time.TotalMinutes;
            return startMinutes < endMinutes
                ? minutes >= startMinutes && minutes < endMinutes
                : minutes >= startMinutes || minutes < endMinutes;
        }
    }
}
