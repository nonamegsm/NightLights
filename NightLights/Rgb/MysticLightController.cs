using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using NightLights; // for AppSettings, Logger (parent namespace isn't visible automatically)

namespace NightLights.Rgb
{
    /// <summary>
    /// Controls the motherboard's built-in RGB (MSI "Mystic Light") through MSI's official,
    /// publicly documented Mystic Light SDK - MysticLight_SDK.dll. Unlike the Kingston side,
    /// nothing here was reverse engineered: this P/Invoke surface matches MSI's published SDK
    /// reference PDF (https://storage-asset.msi.com/file/pdf/Mystic_Light_Software_Development_Kit.pdf).
    ///
    /// Setup required on this PC (see README):
    ///  1. MSI Center (or Mystic Light) installed, with Mystic Light SDK enabled in its settings.
    ///  2. MysticLight_SDK.dll (matching this app's bitness - x64) placed next to NightLights.exe,
    ///     downloaded from MSI's site - it isn't redistributed here since it's MSI's proprietary DLL.
    /// If the DLL or the running service isn't present, every call below simply no-ops and logs
    /// a note - the Kingston DIMM lighting still gets controlled normally either way.
    /// </summary>
    internal sealed class MysticLightController
    {
        private static readonly string CachePath =
            Path.Combine(AppSettings.AppDataFolder, "mystic_light_snapshot.json");

        private bool? _available;

        // Public class + public settable properties (not fields, not private) - so
        // JavaScriptSerializer's reflection-based (de)serializer can always construct
        // and populate it, even though it's only ever used inside this file.
        public sealed class LedRef
        {
            public string Type { get; set; }
            public uint Index { get; set; }
            public uint R { get; set; }
            public uint G { get; set; }
            public uint B { get; set; }
        }

        public bool EnsureInitialized()
        {
            if (_available.HasValue) return _available.Value;

            try
            {
                int status = NativeMethods.MLAPI_Initialize();
                _available = status == 0;
                if (!_available.Value)
                {
                    Logger.Log("MysticLight: MLAPI_Initialize returned status " + status +
                               " (" + NativeMethods.ErrorText(status) + "). Is Mystic Light SDK enabled in MSI Center?");
                }
                return _available.Value;
            }
            catch (DllNotFoundException)
            {
                Logger.Log("MysticLight: MysticLight_SDK.dll not found next to the app - skipping motherboard RGB. " +
                           "Download it from MSI's SDK page and place it beside NightLights.exe to enable this.");
                _available = false;
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log("MysticLight: initialization failed: " + ex.Message);
                _available = false;
                return false;
            }
        }

        public bool RefreshSnapshot()
        {
            if (!EnsureInitialized()) return false;

            try
            {
                int status = NativeMethods.MLAPI_GetDeviceInfo(out string[] devTypes, out string[] ledCounts);
                if (status != 0 || devTypes == null)
                {
                    Logger.Log("MysticLight: GetDeviceInfo failed: " + NativeMethods.ErrorText(status));
                    return false;
                }

                var leds = new List<LedRef>();
                for (int d = 0; d < devTypes.Length; d++)
                {
                    string type = devTypes[d];
                    if (!int.TryParse(ledCounts[d], out int count) || count <= 0) continue;

                    for (uint i = 0; i < count; i++)
                    {
                        int rc = NativeMethods.MLAPI_GetLedColor(type, i, out uint r, out uint g, out uint b);
                        if (rc != 0) continue;
                        leds.Add(new LedRef { Type = type, Index = i, R = r, G = g, B = b });
                    }
                }

                if (leds.Count == 0)
                {
                    Logger.Log("MysticLight: no controllable LEDs reported.");
                    return false;
                }

                Directory.CreateDirectory(AppSettings.AppDataFolder);
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(CachePath, serializer.Serialize(leds));
                Logger.Log($"MysticLight: snapshot saved ({leds.Count} LED zone(s)).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("MysticLight.RefreshSnapshot failed: " + ex.Message);
                return false;
            }
        }

        public bool TurnOff()
        {
            if (!EnsureInitialized()) return false;

            try
            {
                var leds = LoadSnapshotOrCaptureNow();
                if (leds == null || leds.Count == 0) return false;

                int okCount = 0;
                foreach (var led in leds)
                {
                    int rc = NativeMethods.MLAPI_SetLedColor(led.Type, led.Index, 0, 0, 0);
                    if (rc == 0) okCount++;
                }

                Logger.Log($"MysticLight: turned off {okCount}/{leds.Count} LED zone(s).");
                return okCount > 0;
            }
            catch (Exception ex)
            {
                Logger.Log("MysticLight.TurnOff failed: " + ex.Message);
                return false;
            }
        }

        public bool Restore()
        {
            if (!EnsureInitialized()) return false;

            try
            {
                var leds = LoadSnapshot();
                if (leds == null || leds.Count == 0)
                {
                    Logger.Log("MysticLight: no snapshot to restore from.");
                    return false;
                }

                int okCount = 0;
                foreach (var led in leds)
                {
                    int rc = NativeMethods.MLAPI_SetLedColor(led.Type, led.Index, led.R, led.G, led.B);
                    if (rc == 0) okCount++;
                }

                Logger.Log($"MysticLight: restored {okCount}/{leds.Count} LED zone(s).");
                return okCount > 0;
            }
            catch (Exception ex)
            {
                Logger.Log("MysticLight.Restore failed: " + ex.Message);
                return false;
            }
        }

        private List<LedRef> LoadSnapshotOrCaptureNow()
        {
            var existing = LoadSnapshot();
            if (existing != null && existing.Count > 0) return existing;
            RefreshSnapshot();
            return LoadSnapshot();
        }

        private List<LedRef> LoadSnapshot()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<List<LedRef>>(File.ReadAllText(CachePath));
            }
            catch (Exception ex)
            {
                Logger.Log("MysticLight.LoadSnapshot failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Raw P/Invoke surface for MysticLight_SDK.dll, matching MSI's published SDK reference.
        /// Cdecl calling convention and BSTR/SafeArray marshaling as documented by MSI.
        /// </summary>
        private static class NativeMethods
        {
            private const string SdkName = "MysticLight_SDK.dll";

            [DllImport(SdkName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int MLAPI_Initialize();

            [DllImport(SdkName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern int MLAPI_GetDeviceInfo(
                [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] devTypes,
                [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] ledCount);

            [DllImport(SdkName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern int MLAPI_GetLedColor(
                [In, MarshalAs(UnmanagedType.BStr)] string type,
                [In, MarshalAs(UnmanagedType.U4)] uint index,
                [Out, MarshalAs(UnmanagedType.U4)] out uint r,
                [Out, MarshalAs(UnmanagedType.U4)] out uint g,
                [Out, MarshalAs(UnmanagedType.U4)] out uint b);

            [DllImport(SdkName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern int MLAPI_SetLedColor(
                [In, MarshalAs(UnmanagedType.BStr)] string type,
                [In, MarshalAs(UnmanagedType.U4)] uint index,
                [In, MarshalAs(UnmanagedType.U4)] uint r,
                [In, MarshalAs(UnmanagedType.U4)] uint g,
                [In, MarshalAs(UnmanagedType.U4)] uint b);

            [DllImport(SdkName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            private static extern int MLAPI_GetErrorMessage(
                int errorCode,
                [Out, MarshalAs(UnmanagedType.BStr)] out string description);

            public static string ErrorText(int status)
            {
                try
                {
                    if (MLAPI_GetErrorMessage(status, out string desc) == 0 && !string.IsNullOrEmpty(desc))
                        return desc;
                }
                catch { /* best effort */ }
                return "status " + status;
            }
        }
    }
}
