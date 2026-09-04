using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NightLights.Rgb;

namespace NightLights
{
    /// <summary>
    /// The whole app: a tray icon plus a polling timer. No main window - this is an
    /// ApplicationContext so the process has no top-level form to show or accidentally close.
    /// </summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly FuryLightController _fury = new FuryLightController();
        private readonly MysticLightController _mystic = new MysticLightController();

        private AppSettings _settings;
        private bool? _lastAppliedIsNight; // null until the first tick decides
        private bool _busy; // reentrancy guard - a tick that's still running skips the next one
        private DateTime? _lastEnforcedUtc;
        private System.Windows.Forms.Timer _resumeSettleTimer; // one-shot, armed after a sleep/resume

        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _followSunItem;
        private ToolStripMenuItem _forceNightItem;
        private ToolStripMenuItem _forceDayItem;
        private ToolStripMenuItem _runAtStartupItem;

        public TrayContext()
        {
            _settings = AppSettings.Load();

            var menu = new ContextMenuStrip();

            _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());

            _followSunItem = new ToolStripMenuItem("Follow sun automatically", null, (s, e) => SetManualOverride(null));
            _forceNightItem = new ToolStripMenuItem("Force night (lights off) now", null, (s, e) => SetManualOverride(true));
            _forceDayItem = new ToolStripMenuItem("Force day (lights on) now", null, (s, e) => SetManualOverride(false));
            menu.Items.Add(_followSunItem);
            menu.Items.Add(_forceNightItem);
            menu.Items.Add(_forceDayItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("Save current lighting as day profile", null,
                async (s, e) => await SaveDayProfileNowAsync()));
            menu.Items.Add(new ToolStripMenuItem("Set day profile color...", null,
                async (s, e) => await SetDayProfileColorAsync()));

            _runAtStartupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleRunAtStartup());
            _runAtStartupItem.Checked = _settings.RunAtStartup;
            menu.Items.Add(_runAtStartupItem);

            menu.Items.Add(new ToolStripMenuItem("Settings...", null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (s, e) => OpenLogFolder()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApp()));

            _trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "NightLights",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => OpenSettings();

            UpdateMenuChecks();

            _timer = new System.Windows.Forms.Timer { Interval = Math.Max(15, _settings.PollIntervalSeconds) * 1000 };
            _timer.Tick += async (s, e) => await TickAsync();
            _timer.Start();

            // FURY CTRL's own background service - and apparently the motherboard's EC too -
            // can silently restore their last "kept" profile on their own (most noticeably
            // right after the PC wakes from sleep), which quietly turns the lights back on
            // without us touching anything. So on top of the regular poll (which keeps
            // re-sending "off" every tick while it's night, not just once at sunset), we also
            // listen for resume and re-assert a few seconds later once devices have settled.
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Run one tick immediately instead of waiting for the first interval to elapse.
            _ = TickAsync();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;

            Logger.Log("System resumed from sleep - will re-assert lighting state shortly.");

            // Give FuryControllerService / the motherboard EC a few seconds to reinitialize
            // before we send anything - sending immediately after resume tends to just fail.
            _resumeSettleTimer?.Stop();
            _resumeSettleTimer?.Dispose();
            _resumeSettleTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _resumeSettleTimer.Tick += async (s, args) =>
            {
                _resumeSettleTimer.Stop();
                _resumeSettleTimer.Dispose();
                _resumeSettleTimer = null;
                await ForceReapplyAsync().ConfigureAwait(false);
            };
            _resumeSettleTimer.Start();
        }

        /// <summary>Unconditionally sends whatever the current day/night state should be
        /// (turn off, or restore from the cached day profile) - never just a snapshot.
        /// Used after resume-from-sleep, and by any tray action that changes the
        /// day/night decision (Force night/day, Follow sun, closing Settings), so those
        /// always actually apply rather than only reacting on the next natural transition.</summary>
        private async Task ForceReapplyAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                bool isNight = _settings.ManualNightOverride ?? ComputeIsNight(out _, out _);
                UpdateStatusText(isNight);
                await (isNight ? ApplyNightAsync() : ApplyDayAsync()).ConfigureAwait(false);
                _lastAppliedIsNight = isNight;
                _lastEnforcedUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Log("ForceReapplyAsync failed: " + ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private static Icon LoadTrayIcon()
        {
            // Reuses the .exe's own icon (embedded via <ApplicationIcon> in the .csproj),
            // so there's only one icon asset to maintain.
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return SystemIcons.Application; }
        }

        private async Task TickAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                bool isNight = _settings.ManualNightOverride ?? ComputeIsNight(out DateTime? sunrise, out DateTime? sunset);
                UpdateStatusText(isNight);

                if (_lastAppliedIsNight == null)
                {
                    // First tick after launch: apply the current state, but only snapshot a
                    // "day profile" if we're actually starting out in daytime - never overwrite
                    // a good baseline with what might already be an "all off" nighttime state.
                    if (!isNight) await SaveDayProfileNowAsync().ConfigureAwait(false);
                    else await ApplyNightAsync().ConfigureAwait(false);
                    _lastAppliedIsNight = isNight;
                    _lastEnforcedUtc = DateTime.UtcNow;
                }
                else if (isNight != _lastAppliedIsNight.Value)
                {
                    if (isNight)
                    {
                        await _fury.RefreshSnapshotAsync().ConfigureAwait(false);
                        _mystic.RefreshSnapshot();
                        await ApplyNightAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await ApplyDayAsync().ConfigureAwait(false);
                    }
                    _lastAppliedIsNight = isNight;
                    _lastEnforcedUtc = DateTime.UtcNow;
                }
                else if (isNight)
                {
                    // Steady-state night: FURY CTRL's service (and some MSI boards) can
                    // silently reload their own "last kept" profile on their own timeline -
                    // not just on resume - so we keep re-sending "off" every poll instead of
                    // trusting a single command from sunset to stick. This deliberately does
                    // NOT touch the cached day-profile snapshot.
                    await ApplyNightAsync().ConfigureAwait(false);
                    _lastEnforcedUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("TickAsync failed: " + ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private bool ComputeIsNight(out DateTime? sunrise, out DateTime? sunset)
        {
            var now = DateTime.Now;
            var (rise, set) = SunTimes.Calculate(now, _settings.Latitude, _settings.Longitude);
            sunrise = rise;
            sunset = set;

            if (rise == null && set == null) return false; // couldn't compute - default to "day" (safe: lights stay on)
            if (set == null) return false;  // sun never sets today at this latitude/date
            if (rise == null) return true;  // sun never rises today at this latitude/date

            return now < rise.Value || now >= set.Value;
        }

        private async Task ApplyNightAsync()
        {
            var tasks = new System.Collections.Generic.List<Task>();
            if (_settings.ControlFuryDram) tasks.Add(_fury.TurnOffAsync());
            if (_settings.ControlMysticLight) tasks.Add(Task.Run(() => _mystic.TurnOff()));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ApplyDayAsync()
        {
            var tasks = new System.Collections.Generic.List<Task>();
            if (_settings.ControlFuryDram) tasks.Add(_fury.RestoreAsync());
            if (_settings.ControlMysticLight) tasks.Add(Task.Run(() => _mystic.Restore()));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task SaveDayProfileNowAsync()
        {
            var tasks = new System.Collections.Generic.List<Task>();
            if (_settings.ControlFuryDram) tasks.Add(_fury.RefreshSnapshotAsync());
            if (_settings.ControlMysticLight) tasks.Add(Task.Run(() => _mystic.RefreshSnapshot()));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Lets you pick one solid color for the DIMMs/motherboard RGB right from the tray,
        /// instead of having to go into FURY CTRL's own GUI to set up a daytime look. Only
        /// updates the cached "day profile"; ForceReapplyAsync right after is what actually
        /// turns it on (or correctly leaves it off, if it happens to be night right now).
        /// </summary>
        private async Task SetDayProfileColorAsync()
        {
            Color chosen;
            using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                chosen = dlg.Color;
            }

            var tasks = new System.Collections.Generic.List<Task>();
            if (_settings.ControlFuryDram) tasks.Add(_fury.SetStaticColorProfileAsync(chosen.R, chosen.G, chosen.B));
            if (_settings.ControlMysticLight) tasks.Add(Task.Run(() => _mystic.SetStaticColorProfile(chosen.R, chosen.G, chosen.B)));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            await ForceReapplyAsync().ConfigureAwait(false);
        }

        private void UpdateStatusText(bool isNight)
        {
            string mode = _settings.ManualNightOverride.HasValue
                ? (isNight ? "Night (forced)" : "Day (forced)")
                : (isNight ? "Night (auto)" : "Day (auto)");

            _statusItem.Text = "NightLights - " + mode;

            // Kept short: NotifyIcon.Text is capped at 63 characters by Windows.
            string lastEnforced = _lastEnforcedUtc.HasValue
                ? " @" + _lastEnforcedUtc.Value.ToLocalTime().ToString("HH:mm")
                : "";
            _trayIcon.Text = "NightLights - " + mode + lastEnforced;
        }

        private void SetManualOverride(bool? value)
        {
            _settings.ManualNightOverride = value;
            _settings.Save();
            UpdateMenuChecks();
            // ForceReapplyAsync (not a plain TickAsync) - it unconditionally sends the
            // matching apply command. Resetting _lastAppliedIsNight and letting the next
            // regular tick pick it up used to route through TickAsync's "first launch"
            // branch, which for a day result only *snapshots* rather than restoring - so
            // clicking "Force day now" right after "Force night now" looked like it did
            // nothing (and, worse, re-snapshotted the lights-off state as the day profile).
            _ = ForceReapplyAsync();
        }

        private void UpdateMenuChecks()
        {
            _followSunItem.Checked = _settings.ManualNightOverride == null;
            _forceNightItem.Checked = _settings.ManualNightOverride == true;
            _forceDayItem.Checked = _settings.ManualNightOverride == false;
        }

        private void ToggleRunAtStartup()
        {
            _settings.RunAtStartup = !_settings.RunAtStartup;
            AppSettings.ApplyRunAtStartup(_settings.RunAtStartup);
            _settings.Save();
            _runAtStartupItem.Checked = _settings.RunAtStartup;
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    _settings = form.Result;
                    _settings.Save();
                    AppSettings.ApplyRunAtStartup(_settings.RunAtStartup);
                    _timer.Interval = Math.Max(15, _settings.PollIntervalSeconds) * 1000;
                    UpdateMenuChecks();
                    _ = ForceReapplyAsync(); // same reasoning as SetManualOverride - see its comment
                }
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppSettings.AppDataFolder);
                System.Diagnostics.Process.Start("explorer.exe", AppSettings.AppDataFolder);
            }
            catch (Exception ex)
            {
                Logger.Log("OpenLogFolder failed: " + ex.Message);
            }
        }

        private void ExitApp()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _resumeSettleTimer?.Stop();
            _resumeSettleTimer?.Dispose();
            _trayIcon.Visible = false;
            _timer.Stop();
            Application.Exit();
        }
    }
}
