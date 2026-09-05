using System;
using System.Collections.Generic;
using NightLights.Rgb;
using Device = NightLights.Rgb.OpenRgbController.OpenRgbDevice;
using Mode = NightLights.Rgb.OpenRgbController.OpenRgbMode;

namespace NightLights.Tests
{
    internal static class HardwareTests
    {
        public static void Run()
        {
            foreach (var name in new[] { "Direct", "Custom", "Static", "Solid", "Fixed Color", "static color", "Vendor Static", "Fixed_Color", "Static-Color", "Vendor Direct Mode" })
                TestAssert.True(OpenRgbHardware.FindColorMode(DeviceWith(new Mode { Name = name })) != null, name + " is recognized");
            TestAssert.True(OpenRgbHardware.FindColorMode(DeviceWith(new Mode { Name = "Indirect" })) == null, "Token matching avoids indirect false positives");

            var unusual = new Mode { Name = "Vendor lighting", Flags = OpenRgbHardware.PerLedColor | OpenRgbHardware.RandomColor, ColorMode = 3 };
            var device = DeviceWith(unusual);
            TestAssert.Equal(unusual, OpenRgbHardware.FindColorMode(device), "Advertised per-LED mode works regardless of name");
            var prepared = OpenRgbHardware.PrepareColorMode(unusual, 0);
            TestAssert.Equal(1u, prepared.ColorMode, "Random option replaced with per-LED control");
            TestAssert.Equal(3u, unusual.ColorMode, "Capability preparation cannot mutate saved source mode");
            TestAssert.Equal(0, prepared.Colors.Count, "Per-LED mode keeps an empty palette");
            var palette = new Mode { Name = "Custom palette", Flags = OpenRgbHardware.ModeColor | OpenRgbHardware.RandomColor, ColorMode = 3, ColorsMin = 2, ColorsMax = 3 };
            prepared = OpenRgbHardware.PrepareColorMode(palette, 42);
            TestAssert.Equal(2u, prepared.ColorMode, "Mode-specific palette explicitly selected");
            TestAssert.Equal(2, prepared.Colors.Count, "Palette obeys advertised minimum");
            TestAssert.True(prepared.Colors.TrueForAll(c => c == 42), "Every mode color configured");

            device.Modes = new List<Mode> { new Mode { Name = "Static", Flags = OpenRgbHardware.RandomColor, ColorMode = 3 } };
            TestAssert.True(OpenRgbHardware.FindColorMode(device) == null, "Random-only effect isn't accepted just because its name says static");
            device.Modes = new List<Mode> { new Mode { Name = "Spectrum" } };
            TestAssert.True(OpenRgbHardware.Problem(device).Contains("No supported color mode"), "Unsupported reason is actionable");
            device.Modes = new List<Mode> { new Mode { Name = "Direct" } };
            device.Colors.Clear();
            TestAssert.True(OpenRgbHardware.FindColorMode(device) == null, "No colors means no controllable hardware");
            device.Colors.AddRange(new uint[] { 1, 2 });
            TestAssert.True(OpenRgbHardware.Problem(device).Contains("do not match"), "Mismatched buffers rejected");

            var supported = DeviceWith(new Mode { Name = "Fixed" });
            supported.Type = 5;
            var duplicate = DeviceWith(new Mode { Name = "Direct" });
            var unsupported = DeviceWith(new Mode { Name = "Rainbow", Flags = OpenRgbHardware.RandomColor });
            unsupported.Serial = "unsupported";
            supported.Serial = "keyboard";
            string report = OpenRgbHardware.BuildReport(new[] { supported, duplicate, DeviceWith(new Mode { Name = "Static" }), unsupported });
            TestAssert.True(report.Contains("4 device(s), 1 controllable"), "Ambiguous and incompatible devices aren't counted as controllable");
            TestAssert.True(report.Contains("[Keyboard] - 1 LEDs"), "Report shows hardware category and LED count");
            TestAssert.True(report.Contains("via Fixed"), "Report shows compatible mode");
            TestAssert.True(report.Contains("Duplicate device identity"), "Ambiguity is visible before enabling hardware");
            TestAssert.True(report.Contains("random-only"), "Unavailable reason shown per device");
            TestAssert.Equal("Other, type 400", OpenRgbHardware.DeviceType(400), "New device types remain visible without mislabeling");
            TestAssert.True(OpenRgbHardware.BuildReport(new Device[0]).Contains("no RGB controllers"), "Empty hardware list is explained");
            var anonymous = DeviceWith(new Mode { Name = "Direct" });
            anonymous.Name = anonymous.Serial = anonymous.Location = null;
            TestAssert.True(OpenRgbHardware.Problem(anonymous).Contains("No stable OpenRGB identity"), "Missing identity is an explicit incompatibility reason");
            TestAssert.True(OpenRgbHardware.BuildReport(new[] { anonymous }).Contains("1 device(s), 0 controllable"), "Vendor alone is not a stable device identity");
        }

        private static Device DeviceWith(Mode mode) => new Device
        {
            Name = "Example device", Vendor = "Example vendor", Serial = "unique", Location = "USB",
            Leds = new List<OpenRgbController.OpenRgbLed> { new OpenRgbController.OpenRgbLed() },
            Colors = new List<uint> { 0 }, Modes = new List<Mode> { mode }
        };
    }
}
