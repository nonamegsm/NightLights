using System;
using System.Runtime.InteropServices;

namespace NightLights.Audio
{
    /// <summary>
    /// Mutes/unmutes the default Windows playback device (the same thing the volume mixer's
    /// own mute button does) using the public, Microsoft-documented Core Audio API -
    /// IMMDeviceEnumerator / IAudioEndpointVolume, from mmdeviceapi.h and endpointvolume.h.
    /// Nothing reverse engineered here (unlike the Kingston side): this is the standard,
    /// published way .NET apps control system volume, just without a NuGet dependency -
    /// same "raw interop, no external package" approach as MysticLightController.
    /// No admin rights needed.
    /// </summary>
    internal sealed class SystemVolumeController
    {
        private const int ClsCtxAll = 0x17; // CLSCTX_INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER

        public bool Mute() => SetMute(true);

        public bool Unmute() => SetMute(false);

        /// <summary>True/false if known, null if the current state couldn't be read (e.g. no
        /// default playback device).</summary>
        public bool? IsMuted()
        {
            IAudioEndpointVolume endpointVolume = null;
            try
            {
                endpointVolume = GetDefaultEndpointVolume();
                if (endpointVolume == null) return null;
                endpointVolume.GetMute(out bool muted);
                return muted;
            }
            catch (Exception ex)
            {
                Logger.Log("SystemVolumeController.IsMuted failed: " + ex.Message);
                return null;
            }
            finally
            {
                ReleaseCom(endpointVolume);
            }
        }

        private bool SetMute(bool mute)
        {
            IAudioEndpointVolume endpointVolume = null;
            try
            {
                endpointVolume = GetDefaultEndpointVolume();
                if (endpointVolume == null)
                {
                    Logger.Log("Volume: no default playback device found - skipping.");
                    return false;
                }

                endpointVolume.SetMute(mute, Guid.Empty);
                Logger.Log(mute ? "Volume: system audio muted." : "Volume: system audio unmuted.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("SystemVolumeController.SetMute failed: " + ex.Message);
                return false;
            }
            finally
            {
                ReleaseCom(endpointVolume);
            }
        }

        private IAudioEndpointVolume GetDefaultEndpointVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return null;

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o);
                return (IAudioEndpointVolume)o;
            }
            finally
            {
                ReleaseCom(device);
                ReleaseCom(enumerator);
            }
        }

        private static void ReleaseCom(object o)
        {
            if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }

        // --- Core Audio API COM interop surface (public/documented COM interfaces).
        // Every interface member is declared, in the exact vtable order Microsoft documents,
        // even members this class never calls - COM interop dispatches by declaration order,
        // so skipping an unused member would silently misalign every member declared after it. ---

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }

        private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out int pdwState);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(int nChannel, float fLevelDB, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(int nChannel, float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int GetChannelVolumeLevel(int nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(int nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
            int GetVolumeStepInfo(out int pnStep, out int pnStepCount);
            int VolumeStepUp([MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int VolumeStepDown([MarshalAs(UnmanagedType.LPStruct)] Guid pguidEventContext);
            int QueryHardwareSupport(out int pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }
    }
}
