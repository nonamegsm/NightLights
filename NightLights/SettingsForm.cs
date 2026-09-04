using System;
using System.Windows.Forms;

namespace NightLights
{
    public partial class SettingsForm : Form
    {
        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            InitializeComponent();

            Result = current;

            numLatitude.Value = (decimal)current.Latitude;
            numLongitude.Value = (decimal)current.Longitude;
            chkFury.Checked = current.ControlFuryDram;
            chkMystic.Checked = current.ControlMysticLight;
            chkSilenceVolume.Checked = current.SilenceVolumeAtNight;
            chkRunAtStartup.Checked = current.RunAtStartup;
            numPollInterval.Value = Math.Max(numPollInterval.Minimum,
                Math.Min(numPollInterval.Maximum, current.PollIntervalSeconds));

            UpdateSunInfo();
        }

        private void CoordinatesChanged(object sender, EventArgs e) => UpdateSunInfo();

        private void UpdateSunInfo()
        {
            try
            {
                var (sunrise, sunset) = SunTimes.Calculate(DateTime.Now, (double)numLatitude.Value, (double)numLongitude.Value);
                string riseText = sunrise?.ToString("HH:mm") ?? "never (polar night)";
                string setText = sunset?.ToString("HH:mm") ?? "never (polar day)";
                lblSunInfo.Text = $"Today's sunrise/sunset here: {riseText} / {setText}";
            }
            catch
            {
                lblSunInfo.Text = "Today's sunrise/sunset: unable to calculate for these coordinates.";
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            Result = new AppSettings
            {
                Latitude = (double)numLatitude.Value,
                Longitude = (double)numLongitude.Value,
                ControlFuryDram = chkFury.Checked,
                ControlMysticLight = chkMystic.Checked,
                SilenceVolumeAtNight = chkSilenceVolume.Checked,
                RunAtStartup = chkRunAtStartup.Checked,
                PollIntervalSeconds = (int)numPollInterval.Value,
                ManualNightOverride = Result.ManualNightOverride,
                DayProfileBrightness = Result.DayProfileBrightness
            };
        }
    }
}
