using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Device = NightLights.Rgb.OpenRgbController.OpenRgbDevice;
using Mode = NightLights.Rgb.OpenRgbController.OpenRgbMode;

namespace NightLights.Rgb
{
    // Protocol 3 flags and device-type IDs from OpenRGB's RGBController.h:
    // https://gitlab.com/CalcProgrammer1/OpenRGB/-/blob/release_0.9/RGBController/RGBController.h
    internal static class OpenRgbHardware
    {
        internal const uint PerLedColor = 1 << 5;
        internal const uint ModeColor = 1 << 6;
        internal const uint RandomColor = 1 << 7;

        public static Mode FindDirectMode(Device device) => device.Modes.FirstOrDefault(m =>
            CanSetColor(m) && (HasName(m, "direct") || HasName(m, "custom")));

        public static Mode FindStaticMode(Device device) => device.Modes.FirstOrDefault(m =>
            CanSetColor(m) && (HasName(m, "static") || HasName(m, "solid") || HasName(m, "fixed")));

        public static Mode FindColorMode(Device device, bool preferDirect = false)
        {
            if (device.Colors.Count == 0 || device.Colors.Count != device.Leds.Count) return null;
            var named = preferDirect
                ? FindDirectMode(device) ?? FindStaticMode(device)
                : FindStaticMode(device) ?? FindDirectMode(device);
            return named ?? device.Modes.FirstOrDefault(m => CanSetColor(m) && (m.Flags & (PerLedColor | ModeColor)) != 0);
        }

        private static bool CanSetColor(Mode mode)
        {
            if (mode == null) return false;
            // A random-only effect cannot be made black by changing a cached RGB buffer.
            if ((mode.Flags & RandomColor) != 0 && (mode.Flags & (PerLedColor | ModeColor)) == 0) return false;
            if (mode.ColorMode == 3 && (mode.Flags & (PerLedColor | ModeColor)) == 0) return false;
            if ((mode.Flags & ModeColor) != 0 && (mode.Flags & PerLedColor) == 0)
                return mode.ColorsMax >= Math.Max(1u, mode.ColorsMin) && mode.ColorsMax <= ushort.MaxValue;
            return true;
        }

        private static bool HasName(Mode mode, string name)
        {
            string text = new string((mode.Name ?? "").Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, name, StringComparison.OrdinalIgnoreCase));
        }

        public static Mode PrepareColorMode(Mode source, uint color)
        {
            var mode = source.Clone();
            mode.Brightness = Math.Max(mode.BrightnessMin, mode.BrightnessMax);
            int paletteCount = mode.Colors.Count;
            if ((mode.Flags & PerLedColor) != 0) mode.ColorMode = 1;
            else if ((mode.Flags & ModeColor) != 0)
            {
                mode.ColorMode = 2;
                paletteCount = (int)Math.Max(Math.Max(1u, mode.ColorsMin), Math.Min(mode.ColorsMax, (uint)paletteCount));
            }
            // Preserve an empty per-LED palette; the LED buffer is sent separately.
            mode.Colors = Enumerable.Repeat(color, paletteCount).ToList();
            return mode;
        }

        public static string Problem(Device device)
        {
            if (!device.HasStableIdentity)
                return "No stable OpenRGB identity; automatic control is skipped to protect day profiles.";
            if (device.Leds.Count == 0 || device.Colors.Count == 0)
                return "No addressable LEDs reported; configure this device in OpenRGB first.";
            if (device.Leds.Count != device.Colors.Count)
                return "LED and color counts do not match; check the OpenRGB device configuration.";
            if (FindColorMode(device) == null)
                return "No supported color mode; random-only effects cannot be blacked out reliably.";
            return null;
        }

        public static string BuildReport(IReadOnlyList<Device> devices)
        {
            if (devices.Count == 0) return "OpenRGB server answered, but reported no RGB controllers.";
            var duplicates = new HashSet<string>(devices.GroupBy(d => d.StableKey).Where(g => g.Count() > 1).Select(g => g.Key));
            int supported = devices.Count(d => !duplicates.Contains(d.StableKey) && Problem(d) == null);
            var result = new StringBuilder();
            result.AppendLine($"OpenRGB: {devices.Count} device(s), {supported} controllable for night lighting.");
            result.AppendLine("Reported capabilities only; test the lighting on your hardware.");
            foreach (var device in devices)
            {
                result.AppendLine();
                result.AppendLine($"{device.Id + 1}. {Clean(device.DisplayIdentity)} [{DeviceType(device.Type)}] - {device.Leds.Count} LEDs");
                string problem = duplicates.Contains(device.StableKey)
                    ? "Duplicate device identity; automatic control is skipped to protect day profiles."
                    : Problem(device);
                if (problem != null) result.AppendLine("Unavailable: " + problem);
                else result.AppendLine("Night control: available via " + Clean(FindColorMode(device, true).Name) + ".");
                result.AppendLine("Modes: " + (device.Modes.Count == 0 ? "none" : string.Join(", ", device.Modes.Select(m => Clean(m.Name)))));
            }
            return result.ToString().TrimEnd();
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value)
            ? "Unnamed" : new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

        internal static string DeviceType(uint type)
        {
            // These IDs are stable in the SDK. Unknown/new IDs remain visible as numbers.
            string[] names = { "Motherboard", "Memory", "Graphics card", "Cooler", "LED strip", "Keyboard",
                "Mouse", "Mouse mat", "Headset", "Headset stand", "Gamepad", "Light", "Speaker",
                "Virtual device", "Storage", "Case", "Microphone", "Accessory", "Keypad" };
            return type < names.Length ? names[type] : "Other, type " + type;
        }
    }
}
