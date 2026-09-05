using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using NightLights.Rgb;

namespace NightLights
{
    public partial class SettingsForm : Form
    {
        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            InitializeComponent();

            current = current.Copy();
            current.Normalize();
            Result = current;

            numLatitude.Value = (decimal)current.Latitude;
            numLongitude.Value = (decimal)current.Longitude;
            chkFury.Checked = current.ControlFuryDram;
            chkMystic.Checked = current.ControlMysticLight;
            chkOpenRgb.Checked = current.ControlOpenRgb;
            txtOpenRgbHost.Text = current.OpenRgbHost;
            numOpenRgbPort.Value = current.OpenRgbPort;
            cmbSchedule.SelectedIndex = (int)current.ScheduleMode;
            timeQuietStart.Value = DateTime.Today.AddMinutes(current.QuietHoursStartMinutes);
            timeQuietEnd.Value = DateTime.Today.AddMinutes(current.QuietHoursEndMinutes);
            chkPowerSaver.Checked = current.PowerSaverAtNight;
            chkSilenceVolume.Checked = current.SilenceVolumeAtNight;
            chkRunAtStartup.Checked = current.RunAtStartup;
            numPollInterval.Value = Math.Max(numPollInterval.Minimum,
                Math.Min(numPollInterval.Maximum, current.PollIntervalSeconds));

            UpdateSunInfo();
            UpdateEnabledControls();
        }

        private void UpdateEnabledControls()
        {
            if (timeQuietStart == null || txtOpenRgbHost == null) return;
            timeQuietStart.Enabled = timeQuietEnd.Enabled = cmbSchedule.SelectedIndex != 0;
            numLatitude.Enabled = numLongitude.Enabled = cmbSchedule.SelectedIndex != 1;
            txtOpenRgbHost.Enabled = numOpenRgbPort.Enabled = btnProbeOpenRgb.Enabled = chkOpenRgb.Checked;
        }

        private async Task ProbeOpenRgbAsync()
        {
            if (!ValidateOpenRgbHost()) return;
            btnProbeOpenRgb.Enabled = false;
            txtOpenRgbStatus.Text = "Connecting to OpenRGB...";
            try
            {
                var controller = new OpenRgbController(txtOpenRgbHost.Text.Trim(), (int)numOpenRgbPort.Value);
                string status = await controller.ProbeAsync();
                if (!IsDisposed) txtOpenRgbStatus.Text = status;
            }
            catch (Exception ex)
            {
                if (!IsDisposed) txtOpenRgbStatus.Text = "OpenRGB connection failed: " + ex.Message;
            }
            finally
            {
                if (!IsDisposed) UpdateEnabledControls();
            }
        }

        private bool ValidateOpenRgbHost()
        {
            if (AppSettings.IsValidOpenRgbHost(txtOpenRgbHost.Text.Trim())) return true;
            MessageBox.Show(this, "Enter a host name or IP address, without a URL scheme or port.", "OpenRGB server", MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtOpenRgbHost.Focus();
            return false;
        }

        private void CoordinatesChanged(object sender, EventArgs e) => UpdateSunInfo();

        private void UpdateSunInfo()
        {
            try
            {
                var (sunrise, sunset) = SunTimes.Calculate(DateTime.Now, (double)numLatitude.Value, (double)numLongitude.Value);
                if (!sunrise.HasValue && !sunset.HasValue)
                {
                    lblSunInfo.Text = SunTimes.IsNight(DateTime.Now, (double)numLatitude.Value, (double)numLongitude.Value)
                        ? "Polar night: the sun stays below the horizon today."
                        : "Polar day: the sun stays above the horizon today.";
                    return;
                }
                string riseText = sunrise?.ToString("HH:mm") ?? "none today";
                string setText = sunset?.ToString("HH:mm") ?? "none today";
                lblSunInfo.Text = $"Today's sunrise/sunset here: {riseText} / {setText}";
            }
            catch
            {
                lblSunInfo.Text = "Today's sunrise/sunset: unable to calculate for these coordinates.";
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (chkOpenRgb.Checked && !ValidateOpenRgbHost()) return;
            int start = timeQuietStart.Value.Hour * 60 + timeQuietStart.Value.Minute;
            int end = timeQuietEnd.Value.Hour * 60 + timeQuietEnd.Value.Minute;
            if (cmbSchedule.SelectedIndex != 0 && start == end)
            {
                MessageBox.Show(this, "Choose different start and end times for quiet hours.", "Quiet hours", MessageBoxButtons.OK, MessageBoxIcon.Information);
                timeQuietEnd.Focus();
                return;
            }
            Result = new AppSettings
            {
                Latitude = (double)numLatitude.Value,
                Longitude = (double)numLongitude.Value,
                ControlFuryDram = chkFury.Checked,
                ControlMysticLight = chkMystic.Checked,
                ControlOpenRgb = chkOpenRgb.Checked,
                OpenRgbHost = txtOpenRgbHost.Text.Trim(),
                OpenRgbPort = (int)numOpenRgbPort.Value,
                ScheduleMode = (NightScheduleMode)cmbSchedule.SelectedIndex,
                QuietHoursStartMinutes = start,
                QuietHoursEndMinutes = end,
                PowerSaverAtNight = chkPowerSaver.Checked,
                SilenceVolumeAtNight = chkSilenceVolume.Checked,
                RunAtStartup = chkRunAtStartup.Checked,
                PollIntervalSeconds = (int)numPollInterval.Value,
                ManualNightOverride = Result.ManualNightOverride,
                DayProfileBrightness = Result.DayProfileBrightness
            };
            DialogResult = DialogResult.OK;
        }
    }
}
