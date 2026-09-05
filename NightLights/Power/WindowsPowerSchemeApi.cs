using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NightLights.Power
{
    internal sealed class WindowsPowerSchemeApi : IPowerSchemeApi
    {
        private const uint ErrorSuccess = 0;

        public bool TryGetActiveScheme(out Guid schemeGuid, out string error)
        {
            IntPtr guidPtr = IntPtr.Zero;

            try
            {
                uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out guidPtr);
                if (result != ErrorSuccess)
                {
                    schemeGuid = Guid.Empty;
                    error = FormatError(result);
                    return false;
                }

                schemeGuid = (Guid)Marshal.PtrToStructure(guidPtr, typeof(Guid));
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                schemeGuid = Guid.Empty;
                error = ex.Message;
                return false;
            }
            finally
            {
                if (guidPtr != IntPtr.Zero)
                {
                    NativeMethods.LocalFree(guidPtr);
                }
            }
        }

        public bool TrySetActiveScheme(Guid schemeGuid, out string error)
        {
            try
            {
                uint result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
                if (result == ErrorSuccess)
                {
                    error = null;
                    return true;
                }

                error = FormatError(result);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string FormatError(uint result)
        {
            return new Win32Exception(unchecked((int)result)).Message + " (" + result + ")";
        }

        private static class NativeMethods
        {
            [DllImport("powrprof.dll")]
            public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

            [DllImport("powrprof.dll")]
            public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

            [DllImport("kernel32.dll")]
            public static extern IntPtr LocalFree(IntPtr memory);
        }
    }
}
