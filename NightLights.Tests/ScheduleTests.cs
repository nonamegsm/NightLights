using System;

namespace NightLights.Tests
{
    internal static class ScheduleTests
    {
        public static void Run()
        {
            var settings = new AppSettings { ScheduleMode = NightScheduleMode.QuietHours };
            var date = new DateTime(2026, 9, 5);
            TestAssert.True(!NightSchedule.IsNight(settings, date.AddHours(21).AddMinutes(59)), "Before start is day");
            TestAssert.True(NightSchedule.IsNight(settings, date.AddHours(22)), "Start is inclusive");
            TestAssert.True(NightSchedule.IsNight(settings, date), "Quiet hours cross midnight");
            TestAssert.True(NightSchedule.IsNight(settings, date.AddHours(7).AddTicks(-1)), "End has not yet arrived");
            TestAssert.True(!NightSchedule.IsNight(settings, date.AddHours(7)), "End is exclusive");
            settings.QuietHoursStartMinutes = 10 * 60;
            settings.QuietHoursEndMinutes = 12 * 60;
            TestAssert.True(NightSchedule.IsNight(settings, date.AddHours(11)), "Same-day quiet period");
            TestAssert.True(!NightSchedule.IsNight(settings, date), "Midnight outside same-day period");
            settings.ManualNightOverride = true;
            TestAssert.True(NightSchedule.IsNight(settings, date), "Force night overrides schedule");
            settings.ManualNightOverride = false;
            TestAssert.True(!NightSchedule.IsNight(settings, date.AddHours(11)), "Force day overrides schedule");
            settings.ManualNightOverride = null;
            settings.ScheduleMode = NightScheduleMode.SunOrQuietHours;
            settings.Latitude = 89;
            settings.Longitude = 0;
            TestAssert.True(NightSchedule.IsNight(settings, new DateTime(2026, 6, 21, 11, 0, 0)), "Quiet hours apply even during polar day");
            TestAssert.True(!NightSchedule.IsNight(settings, new DateTime(2026, 6, 21, 15, 0, 0)), "Polar day outside quiet period");
            TestAssert.True(NightSchedule.IsNight(settings, new DateTime(2026, 12, 21, 15, 0, 0)), "Polar night applies outside quiet period");
            settings.ScheduleMode = NightScheduleMode.FollowSun;
            TestAssert.True(!NightSchedule.IsNight(settings, new DateTime(2026, 6, 21, 11, 0, 0)), "Sun-only ignores quiet period");
            TestAssert.True(SunTimes.IsNight(new DateTime(2026, 6, 21), -89, 0), "Southern polar winter");
            TestAssert.True(!SunTimes.IsNight(new DateTime(2026, 12, 21), -89, 0), "Southern polar summer");
            var sun = SunTimes.Calculate(date, 52.2297, 21.0122);
            TestAssert.True(!SunTimes.IsNight(sun.Sunrise.Value, 52.2297, 21.0122), "Sunrise ends night");
            TestAssert.True(SunTimes.IsNight(sun.Sunset.Value, 52.2297, 21.0122), "Sunset starts night");
            for (int day = 0; day < 365; day++)
            {
                var polarDate = new DateTime(2026, 1, 1).AddDays(day);
                var events = SunTimes.Calculate(polarDate, 70, 0);
                if (events.Sunrise.HasValue == events.Sunset.HasValue) continue;
                var solarEvent = (events.Sunrise ?? events.Sunset).Value;
                TestAssert.Equal(events.Sunrise.HasValue, SunTimes.IsNight(solarEvent.AddSeconds(-1), 70, 0), "One-event polar transition before the event");
                TestAssert.Equal(events.Sunset.HasValue, SunTimes.IsNight(solarEvent, 70, 0), "One-event polar transition at the event");
            }
        }
    }
}
