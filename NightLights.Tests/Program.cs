using System;
using System.Collections.Generic;

namespace NightLights.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var suites = new Dictionary<string, Action>
            {
                { "Schedules", ScheduleTests.Run },
                { "Settings", SettingsTests.Run },
                { "Lighting transitions", LightingTests.Run },
                { "Power recovery", PowerTests.Run },
                { "OpenRGB protocol", OpenRgbTests.Run },
                { "Hardware compatibility", HardwareTests.Run }
            };
            int failures = 0;
            foreach (var suite in suites)
            {
                try { suite.Value(); Console.WriteLine("PASS " + suite.Key); }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + suite.Key + ": " + ex); }
            }
            Console.WriteLine(TestAssert.Count + " assertions, " + failures + " failed suite(s). No physical devices or power plans changed.");
            return failures == 0 ? 0 : 1;
        }
    }

    internal static class TestAssert
    {
        public static int Count { get; private set; }
        public static void True(bool value, string message)
        {
            Count++;
            if (!value) throw new Exception(message);
        }
        public static void Equal<T>(T expected, T actual, string message) =>
            True(EqualityComparer<T>.Default.Equals(expected, actual), message + " (expected " + expected + ", actual " + actual + ")");
        public static void Throws<T>(Action action, string message) where T : Exception
        {
            Count++;
            try { action(); }
            catch (T) { return; }
            throw new Exception(message + ": expected " + typeof(T).Name);
        }
    }
}
