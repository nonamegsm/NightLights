using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NightLights.Rgb
{
    // TrayContext serializes calls. Each module fails independently, so a missing SDK
    // cannot prevent another module from keeping the room dark.
    internal sealed class LightingCoordinator
    {
        private bool? _lastIsNight;
        private readonly HashSet<ILightingModule> _restorePending = new HashSet<ILightingModule>();
        public string Status { get; private set; } = "Lighting: starting";

        public async Task ApplyAsync(IReadOnlyList<ILightingModule> modules, bool isNight, bool force, bool captureBeforeNight = true)
        {
            var failures = new List<string>();
            foreach (var module in modules)
            {
                bool ok = true;
                if (isNight)
                {
                    if (_lastIsNight == false && captureBeforeNight)
                        ok = await RunAsync(module, m => m.RefreshSnapshotAsync());
                    ok = await RunAsync(module, m => m.TurnOffAsync()) && ok;
                    _restorePending.Add(module);
                }
                else if (force || _lastIsNight == true || _restorePending.Contains(module))
                {
                    ok = await RunAsync(module, m => m.RestoreAsync());
                    if (ok) _restorePending.Remove(module);
                    else _restorePending.Add(module);
                }
                else if (_lastIsNight == null)
                {
                    ok = await RunAsync(module, m => m.RefreshSnapshotAsync());
                }
                if (!ok) failures.Add(module.Name);
            }
            _lastIsNight = isNight;
            Status = failures.Count > 0 ? "Lighting unavailable: " + string.Join(", ", failures)
                : modules.Count == 0 ? "Lighting: no modules enabled"
                : "Lighting: " + modules.Count + " module(s) enabled";
        }

        public async Task SaveAsync(IReadOnlyList<ILightingModule> modules)
        {
            foreach (var module in modules) await RunAsync(module, m => m.RefreshSnapshotAsync());
        }

        public async Task SetColorAsync(IReadOnlyList<ILightingModule> modules, byte r, byte g, byte b, int brightness)
        {
            foreach (var module in modules)
                await RunAsync(module, m => m.SetStaticColorProfileAsync(r, g, b, brightness));
        }

        internal static async Task<bool> RunAsync(ILightingModule module, Func<ILightingModule, Task<bool>> action)
        {
            try { return await action(module); }
            catch (Exception ex)
            {
                Logger.Log(module.Name + " module failed: " + ex.Message);
                return false;
            }
        }
    }
}
