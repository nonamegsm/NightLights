using System;
using System.IO;

namespace NightLights.Power
{
    public sealed class PowerPlanController
    {
        public static readonly Guid PowerSaverSchemeGuid = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        private readonly IPowerSchemeApi _api;
        private readonly string _statePath;
        private readonly Action<string> _log;
        private readonly object _gate = new object();

        public string Status { get; private set; }

        public PowerPlanController()
            : this(
                new WindowsPowerSchemeApi(),
                Path.Combine(AppSettings.AppDataFolder, "power-plan-restore.state"),
                Logger.Log)
        {
        }

        public PowerPlanController(IPowerSchemeApi api, string statePath, Action<string> log)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (string.IsNullOrWhiteSpace(statePath)) throw new ArgumentException("State path is required.", nameof(statePath));

            _api = api;
            _statePath = statePath;
            _log = log ?? (_ => { });
            Status = "Power saving idle.";
        }

        public bool Apply(bool enabled, bool isNight)
        {
            lock (_gate)
            {
                return ApplyCore(enabled, isNight);
            }
        }

        public bool Restore()
        {
            lock (_gate)
            {
                return RestoreCore();
            }
        }

        private bool ApplyCore(bool enabled, bool isNight)
        {
            if (!enabled || !isNight)
            {
                return RestoreCore();
            }

            PowerPlanRestoreState state;
            string loadError;
            PowerPlanRestoreStateLoadStatus loadStatus = PowerPlanRestoreState.Load(_statePath, out state, out loadError);
            if (loadStatus == PowerPlanRestoreStateLoadStatus.Error)
            {
                Status = "Could not read power-plan restore state: " + loadError;
                _log(Status);
                return false;
            }

            Guid active;
            if (!TryGetActiveScheme(out active))
            {
                return false;
            }

            if (loadStatus == PowerPlanRestoreStateLoadStatus.Loaded)
            {
                if (active == state.ManagedSchemeGuid)
                {
                    if (!state.ChangeApplied)
                    {
                        state.ChangeApplied = true;
                        TrySaveState(state);
                    }

                    Status = "Power saver is active; previous power plan is saved.";
                    return true;
                }

                if (state.ChangeApplied)
                {
                    Status = "Power saver paused because the active power plan was changed manually.";
                    return true;
                }

                if (active != state.OriginalSchemeGuid)
                {
                    Status = "Power saver suppressed because the active power plan changed manually before it was applied.";
                    return true;
                }

                return SetManagedScheme(state);
            }

            if (active == PowerSaverSchemeGuid)
            {
                Status = "Power saver is already active.";
                return true;
            }

            state = new PowerPlanRestoreState
            {
                OriginalSchemeGuid = active,
                ManagedSchemeGuid = PowerSaverSchemeGuid,
                ChangeApplied = false,
                CreatedUtcTicks = DateTime.UtcNow.Ticks
            };

            if (!TrySaveState(state))
            {
                return false;
            }

            return SetManagedScheme(state);
        }

        private bool RestoreCore()
        {
            PowerPlanRestoreState state;
            string loadError;
            PowerPlanRestoreStateLoadStatus loadStatus = PowerPlanRestoreState.Load(_statePath, out state, out loadError);
            if (loadStatus == PowerPlanRestoreStateLoadStatus.Missing)
            {
                Status = "Power saving idle.";
                return true;
            }

            if (loadStatus == PowerPlanRestoreStateLoadStatus.Error)
            {
                Status = "Could not read power-plan restore state: " + loadError;
                _log(Status);
                return false;
            }

            Guid active;
            if (!TryGetActiveScheme(out active))
            {
                return false;
            }

            if (active == state.OriginalSchemeGuid)
            {
                DeleteState();
                Status = "Original power plan is already active.";
                return true;
            }

            if (active != state.ManagedSchemeGuid)
            {
                DeleteState();
                Status = "Power plan changed manually; original plan was not restored.";
                return true;
            }

            string error;
            if (!TrySetActiveScheme(state.OriginalSchemeGuid, out error))
            {
                Status = "Could not restore previous power plan: " + error;
                _log(Status);
                return false;
            }

            DeleteState();
            Status = "Previous power plan restored.";
            return true;
        }

        private bool SetManagedScheme(PowerPlanRestoreState state)
        {
            string error;
            if (!TrySetActiveScheme(state.ManagedSchemeGuid, out error))
            {
                Status = "Could not switch to Power saver: " + error;
                _log(Status);
                return false;
            }

            state.ChangeApplied = true;
            TrySaveState(state);
            Status = "Power saver active until morning.";
            return true;
        }

        private bool TryGetActiveScheme(out Guid active)
        {
            string error = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (_api.TryGetActiveScheme(out active, out error))
                {
                    return true;
                }
            }

            active = Guid.Empty;
            Status = "Could not read active power plan: " + error;
            _log(Status);
            return false;
        }

        private bool TrySetActiveScheme(Guid schemeGuid, out string error)
        {
            error = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (_api.TrySetActiveScheme(schemeGuid, out error))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySaveState(PowerPlanRestoreState state)
        {
            string tempPath = null;

            try
            {
                string directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                tempPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(state.Serialize());
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(_statePath))
                {
                    File.Replace(tempPath, _statePath, null);
                }
                else
                {
                    File.Move(tempPath, _statePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Status = "Could not save power-plan restore state: " + ex.Message;
                _log(Status);
                return false;
            }
            finally
            {
                try
                {
                    if (tempPath != null && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    _log("Could not delete temporary power-plan restore state: " + ex.Message);
                }
            }
        }

        private void DeleteState()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    File.Delete(_statePath);
                }
            }
            catch (Exception ex)
            {
                _log("Could not delete power-plan restore state: " + ex.Message);
            }
        }
    }
}
