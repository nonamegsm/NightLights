using System;

namespace NightLights
{
    /// <summary>
    /// Sunrise / sunset calculator using the standard NOAA solar position algorithm
    /// (the same formulas behind NOAA's published solar calculator). Works fully
    /// offline - no network calls, no external services, accurate to within about
    /// a minute for civil sunrise/sunset almost anywhere on Earth.
    /// </summary>
    internal static class SunTimes
    {
        /// <summary>
        /// Returns local sunrise and sunset for the given date and location.
        /// Either value can be null for locations/dates with no sunrise or no sunset.
        /// Use IsNight for the policy decision; both events are absent during either
        /// polar day or polar night, so null values alone cannot distinguish them.
        /// </summary>
        public static (DateTime? Sunrise, DateTime? Sunset) Calculate(DateTime localDate, double latitude, double longitude)
        {
            double utcOffsetHours = TimeZoneInfo.Local.GetUtcOffset(localDate).TotalHours;

            DateTime? sunrise = SolarEvent(localDate, latitude, longitude, utcOffsetHours, true, out _);
            DateTime? sunset = SolarEvent(localDate, latitude, longitude, utcOffsetHours, false, out _);
            return (sunrise, sunset);
        }

        public static bool IsNight(DateTime localNow, double latitude, double longitude)
        {
            double offset = TimeZoneInfo.Local.GetUtcOffset(localNow).TotalHours;
            var rise = SolarEvent(localNow, latitude, longitude, offset, true, out bool polarNight);
            var set = SolarEvent(localNow, latitude, longitude, offset, false, out _);
            if (!rise.HasValue && !set.HasValue) return polarNight;
            if (!rise.HasValue) return localNow >= set.Value;
            if (!set.HasValue) return localNow < rise.Value;
            // Locations in another time zone can have daylight spanning local midnight.
            return rise.Value <= set.Value
                ? localNow < rise.Value || localNow >= set.Value
                : localNow >= set.Value && localNow < rise.Value;
        }

        private static DateTime? SolarEvent(DateTime date, double lat, double lon, double utcOffsetHours, bool isSunrise, out bool polarNight)
        {
            polarNight = false;
            const double zenith = 90.833; // official sunrise/sunset (includes atmospheric refraction + solar disk radius)

            int dayOfYear = date.DayOfYear;

            double lngHour = lon / 15.0;
            double t = isSunrise
                ? dayOfYear + ((6 - lngHour) / 24.0)
                : dayOfYear + ((18 - lngHour) / 24.0);

            double meanAnomaly = (0.9856 * t) - 3.289;

            double trueLongitude = meanAnomaly
                + (1.916 * Sin(meanAnomaly))
                + (0.020 * Sin(2 * meanAnomaly))
                + 282.634;
            trueLongitude = NormalizeDegrees(trueLongitude);

            double rightAscension = ToDegrees(Math.Atan(0.91764 * Tan(trueLongitude)));
            rightAscension = NormalizeDegrees(rightAscension);

            double lQuadrant = Math.Floor(trueLongitude / 90.0) * 90.0;
            double raQuadrant = Math.Floor(rightAscension / 90.0) * 90.0;
            rightAscension += (lQuadrant - raQuadrant);
            rightAscension /= 15.0;

            double sinDec = 0.39782 * Sin(trueLongitude);
            double cosDec = Cos(ToDegrees(Math.Asin(sinDec)));

            double cosH = (Cos(zenith) - (sinDec * Sin(lat))) / (cosDec * Cos(lat));

            if (cosH > 1) { polarNight = true; return null; }  // sun never rises here today
            if (cosH < -1) return null; // sun never sets this day at this location

            double h = isSunrise
                ? 360 - ToDegrees(Math.Acos(cosH))
                : ToDegrees(Math.Acos(cosH));
            h /= 15.0;

            double localMeanTime = h + rightAscension - (0.06571 * t) - 6.622;

            double utcTime = localMeanTime - lngHour;
            utcTime = ((utcTime % 24) + 24) % 24;

            double localTime = utcTime + utcOffsetHours;
            localTime = ((localTime % 24) + 24) % 24;

            int hours = (int)Math.Floor(localTime);
            int minutes = (int)Math.Floor((localTime - hours) * 60);
            int seconds = (int)Math.Round((((localTime - hours) * 60) - minutes) * 60);
            if (seconds == 60) { seconds = 0; minutes++; }
            if (minutes == 60) { minutes = 0; hours++; }
            hours = ((hours % 24) + 24) % 24;

            return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, date.Kind)
                .AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
        }

        private static double NormalizeDegrees(double deg)
        {
            deg = deg % 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }

        private static double ToRadians(double deg) => deg * Math.PI / 180.0;
        private static double ToDegrees(double rad) => rad * 180.0 / Math.PI;
        private static double Sin(double deg) => Math.Sin(ToRadians(deg));
        private static double Cos(double deg) => Math.Cos(ToRadians(deg));
        private static double Tan(double deg) => Math.Tan(ToRadians(deg));
    }
}
