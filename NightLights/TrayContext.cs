using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NightLights.Audio;
using NightLights.Power;
using NightLights.Rgb;

namespace NightLights
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly TrayModeIcons _modeIcons = new TrayModeIcons();
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Control _dispatcher = new Control();
        private readonly SemaphoreSlim _operation = new SemaphoreSlim(1, 1);
        private readonly ILightingModule _fury = new FuryLightingModule();
        private readonly ILightingModule _mystic = new MysticLightingModule();
        private readonly LightingCoordinator _lighting = new LightingCoordinator();
        private readonly SystemVolumeController _volume = new SystemVolumeController();
        private readonly PowerPlanController _power = new PowerPlanController();
        private readonly object _powerPolicyLock = new object();
        private OpenRgbController _openRgb;
        private AppSettings _settings;
        private bool? _lastIsNight;
        private bool? _displayedIsNight;
        private volatile bool _exiting;
        private System.Windows.Forms.Timer _resumeSettleTimer;
        private readonly ToolStripMenuItem _statusItem, _lightingStatusItem, _powerStatusItem;
        private readonly ToolStripMenuItem _followScheduleItem, _forceNightItem, _forceDayItem, _runAtStartupItem;

        public TrayContext()
        {
            _settings = AppSettings.Load();
            _openRgb = new OpenRgbController(_settings.OpenRgbHost, _settings.OpenRgbPort);
            // Create a UI-thread dispatch handle before subscribing to OS events.
            var handle = _dispatcher.Handle;
            var menu = new ContextMenuStrip();
            _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
            _lightingStatusItem = new ToolStripMenuItem("Lighting: starting") { Enabled = false };
            _powerStatusItem = new ToolStripMenuItem("Power saver: disabled") { Enabled = false };
            menu.Items.AddRange(new ToolStripItem[] { _statusItem, _lightingStatusItem, _powerStatusItem, new ToolStripSeparator() });
            _followScheduleItem = new ToolStripMenuItem("Follow schedule automatically", null, async (s, e) => await SetManualOverrideAsync(null));
            _forceNightItem = new ToolStripMenuItem("Force night now", null, async (s, e) => await SetManualOverrideAsync(true));
            _forceDayItem = new ToolStripMenuItem("Force day now", null, async (s, e) => await SetManualOverrideAsync(false));
            menu.Items.AddRange(new ToolStripItem[] { _followScheduleItem, _forceNightItem, _forceDayItem, new ToolStripSeparator() });
            menu.Items.Add(new ToolStripMenuItem("Save current lighting as day profile", null,
                async (s, e) => await RunExclusiveAsync(() => _lighting.SaveAsync(EnabledLighting()))));
            menu.Items.Add(new ToolStripMenuItem("Set day profile color...", null, async (s, e) => await SetDayProfileColorAsync()));
            _runAtStartupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleRunAtStartup());
            menu.Items.Add(_runAtStartupItem);
            menu.Items.Add(new ToolStripMenuItem("Settings...", null, async (s, e) => await OpenSettingsAsync()));
            menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (s, e) => OpenLogFolder()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, async (s, e) => await ExitAppAsync()));

            _trayIcon = new NotifyIcon { ContextMenuStrip = menu };
            UpdateTrayMode(NightSchedule.IsNight(_settings, DateTime.Now));
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += async (s, e) => await OpenSettingsAsync();
            UpdateMenuChecks();
            _timer = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalSeconds * 1000 };
            _timer.Tick += async (s, e) => await TickAsync();
            _timer.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            _ = TickAsync();
        }

        private IReadOnlyList<ILightingModule> EnabledLighting()
        {
            var modules = new List<ILightingModule>();
            if (_settings.ControlFuryDram) modules.Add(_fury);
            if (_settings.ControlMysticLight) modules.Add(_mystic);
            if (_settings.ControlOpenRgb) modules.Add(_openRgb);
            return modules;
        }

        private async Task RunExclusiveAsync(Func<Task> action, bool skipIfBusy = false)
        {
            if (_exiting) return;
            if (skipIfBusy)
            {
                if (!await _operation.WaitAsync(0)) return;
            }
            else await _operation.WaitAsync();
            try
            {
                if (!_exiting) await action();
            }
            catch (Exception ex) { Logger.Log("NightLights operation failed: " + ex); }
            finally { _operation.Release(); }
        }

        private Task TickAsync() => RunExclusiveAsync(() => ApplyPolicyAsync(false), true);

        private async Task ApplyPolicyAsync(bool force, bool captureBeforeNight = true)
        {
            bool isNight = NightSchedule.IsNight(_settings, DateTime.Now);
            // Show the active decision immediately, even when a hardware module is
            // slow or unavailable. _lastIsNight separately tracks applied policies.
            UpdateTrayMode(isNight);
            // Run power policy on every poll: it owns transition tracking and retries,
            // and recovers an outstanding restore even if the module is now disabled.
            await Task.Run(() =>
            {
                lock (_powerPolicyLock)
                {
                    if (!_exiting) _power.Apply(_settings.PowerSaverAtNight, isNight);
                }
            });
            if (_exiting) return;
            await _lighting.ApplyAsync(EnabledLighting(), isNight, force, captureBeforeNight);
            if (_settings.SilenceVolumeAtNight && (force || _lastIsNight != isNight))
            {
                await Task.Run(() => { if (isNight) _volume.Mute(); else _volume.Unmute(); });
            }
            _lastIsNight = isNight;
            _lightingStatusItem.Text = _lighting.Status;
            _powerStatusItem.Text = _power.Status;
        }

        private void UpdateTrayMode(bool isNight)
        {
            if (_displayedIsNight != isNight)
            {
                _trayIcon.Icon = _modeIcons.ForNight(isNight);
                _displayedIsNight = isNight;
            }
            string mode = (isNight ? "Night" : "Day") + (_settings.ManualNightOverride.HasValue ? " (forced)" : " (auto)");
            _statusItem.Text = "NightLights - " + mode;
            _trayIcon.Text = "NightLights - " + mode;
        }

        private Task SetManualOverrideAsync(bool? value) => RunExclusiveAsync(async () =>
        {
            _settings.ManualNightOverride = value;
            _settings.Save();
            UpdateMenuChecks();
            await ApplyPolicyAsync(true);
        });

        private async Task SetDayProfileColorAsync()
        {
            Color chosen;
            int brightness;
            using (var dialog = new DayProfileColorForm(Color.White, _settings.DayProfileBrightness))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                chosen = dialog.Color;
                brightness = dialog.BrightnessPercent;
            }
            await RunExclusiveAsync(async () =>
            {
                _settings.DayProfileBrightness = brightness;
                _settings.Save();
                await _lighting.SetColorAsync(EnabledLighting(), chosen.R, chosen.G, chosen.B, brightness);
                await ApplyPolicyAsync(true, false);
            });
        }

        private async Task OpenSettingsAsync()
        {
            AppSettings updated;
            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog() != DialogResult.OK) return;
                updated = form.Result;
            }
            await RunExclusiveAsync(async () =>
            {
                bool endpointChanged = !string.Equals(updated.OpenRgbHost, _settings.OpenRgbHost, StringComparison.OrdinalIgnoreCase)
                    || updated.OpenRgbPort != _settings.OpenRgbPort;
                // Release devices from a module before disabling it or changing its server.
                if (_lastIsNight == true)
                {
                    if (_settings.ControlFuryDram && !updated.ControlFuryDram)
                        await LightingCoordinator.RunAsync(_fury, m => m.RestoreAsync());
                    if (_settings.ControlMysticLight && !updated.ControlMysticLight)
                        await LightingCoordinator.RunAsync(_mystic, m => m.RestoreAsync());
                    if (_settings.ControlOpenRgb && (!updated.ControlOpenRgb || endpointChanged))
                        await LightingCoordinator.RunAsync(_openRgb, m => m.RestoreAsync());
                }
                _settings = updated;
                if (endpointChanged) _openRgb = new OpenRgbController(_settings.OpenRgbHost, _settings.OpenRgbPort);
                _settings.Save();
                AppSettings.ApplyRunAtStartup(_settings.RunAtStartup);
                _timer.Interval = _settings.PollIntervalSeconds * 1000;
                UpdateMenuChecks();
                await ApplyPolicyAsync(true);
            });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume || _exiting) return;
            try
            {
                _dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_exiting) return;
                    Logger.Log("System resumed - reapplying night policies after devices settle.");
                    _resumeSettleTimer?.Stop();
                    _resumeSettleTimer?.Dispose();
                    _resumeSettleTimer = new System.Windows.Forms.Timer { Interval = 10000 };
                    _resumeSettleTimer.Tick += async (s, args) =>
                    {
                        _resumeSettleTimer.Stop();
                        _resumeSettleTimer.Dispose();
                        _resumeSettleTimer = null;
                        await RunExclusiveAsync(() => ApplyPolicyAsync(true));
                    };
                    _resumeSettleTimer.Start();
                }));
            }
            catch (InvalidOperationException) { /* UI is already closing. */ }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            // Shutdown can end the message loop before an async exit completes. The
            // controller also retains its recovery record if this attempt fails.
            _exiting = true;
            lock (_powerPolicyLock) _power.Restore();
        }

        private async Task ExitAppAsync()
        {
            if (_exiting) return;
            _exiting = true;
            _timer.Stop();
            _resumeSettleTimer?.Stop();
            await _operation.WaitAsync();
            try { await Task.Run(() => _power.Restore()); }
            finally
            {
                _operation.Release();
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionEnding -= OnSessionEnding;
                _resumeSettleTimer?.Dispose();
                _timer.Dispose();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _modeIcons.Dispose();
                _dispatcher.Dispose();
                Application.Exit();
            }
        }

        private void UpdateMenuChecks()
        {
            _followScheduleItem.Checked = _settings.ManualNightOverride == null;
            _forceNightItem.Checked = _settings.ManualNightOverride == true;
            _forceDayItem.Checked = _settings.ManualNightOverride == false;
            _runAtStartupItem.Checked = _settings.RunAtStartup;
        }

        private void ToggleRunAtStartup()
        {
            _settings.RunAtStartup = !_settings.RunAtStartup;
            AppSettings.ApplyRunAtStartup(_settings.RunAtStartup);
            _settings.Save();
            UpdateMenuChecks();
        }

        private static void OpenLogFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppSettings.AppDataFolder);
                System.Diagnostics.Process.Start("explorer.exe", AppSettings.AppDataFolder);
            }
            catch (Exception ex) { Logger.Log("OpenLogFolder failed: " + ex.Message); }
        }
    }
}
