namespace NightLights
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private System.Windows.Forms.Label lblLatitude;
        private System.Windows.Forms.Label lblLongitude;
        private System.Windows.Forms.NumericUpDown numLatitude;
        private System.Windows.Forms.NumericUpDown numLongitude;
        private System.Windows.Forms.Label lblSunInfo;
        private System.Windows.Forms.CheckBox chkFury;
        private System.Windows.Forms.CheckBox chkMystic;
        private System.Windows.Forms.CheckBox chkSilenceVolume;
        private System.Windows.Forms.CheckBox chkRunAtStartup;
        private System.Windows.Forms.Label lblPollInterval;
        private System.Windows.Forms.NumericUpDown numPollInterval;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblHint;

        private void InitializeComponent()
        {
            this.lblLatitude = new System.Windows.Forms.Label();
            this.lblLongitude = new System.Windows.Forms.Label();
            this.numLatitude = new System.Windows.Forms.NumericUpDown();
            this.numLongitude = new System.Windows.Forms.NumericUpDown();
            this.lblSunInfo = new System.Windows.Forms.Label();
            this.chkFury = new System.Windows.Forms.CheckBox();
            this.chkMystic = new System.Windows.Forms.CheckBox();
            this.chkSilenceVolume = new System.Windows.Forms.CheckBox();
            this.chkRunAtStartup = new System.Windows.Forms.CheckBox();
            this.lblPollInterval = new System.Windows.Forms.Label();
            this.numPollInterval = new System.Windows.Forms.NumericUpDown();
            this.btnOk = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblHint = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numLatitude)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLongitude)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollInterval)).BeginInit();
            this.SuspendLayout();

            // lblHint
            this.lblHint.AutoSize = true;
            this.lblHint.Location = new System.Drawing.Point(12, 12);
            this.lblHint.MaximumSize = new System.Drawing.Size(360, 0);
            this.lblHint.Text = "Enter your location so sunrise/sunset can be calculated offline. " +
                "(Tip: search \"my coordinates\" in a map app.)";
            this.lblHint.Size = new System.Drawing.Size(360, 32);

            // lblLatitude
            this.lblLatitude.AutoSize = true;
            this.lblLatitude.Location = new System.Drawing.Point(12, 54);
            this.lblLatitude.Text = "Latitude:";

            // numLatitude
            this.numLatitude.DecimalPlaces = 5;
            this.numLatitude.Location = new System.Drawing.Point(110, 52);
            this.numLatitude.Minimum = new decimal(new int[] { 90, 0, 0, System.Int32.MinValue });
            this.numLatitude.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            this.numLatitude.Size = new System.Drawing.Size(110, 20);
            this.numLatitude.ValueChanged += new System.EventHandler(this.CoordinatesChanged);

            // lblLongitude
            this.lblLongitude.AutoSize = true;
            this.lblLongitude.Location = new System.Drawing.Point(12, 82);
            this.lblLongitude.Text = "Longitude:";

            // numLongitude
            this.numLongitude.DecimalPlaces = 5;
            this.numLongitude.Location = new System.Drawing.Point(110, 80);
            this.numLongitude.Minimum = new decimal(new int[] { 180, 0, 0, System.Int32.MinValue });
            this.numLongitude.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            this.numLongitude.Size = new System.Drawing.Size(110, 20);
            this.numLongitude.ValueChanged += new System.EventHandler(this.CoordinatesChanged);

            // lblSunInfo
            this.lblSunInfo.AutoSize = true;
            this.lblSunInfo.Location = new System.Drawing.Point(12, 110);
            this.lblSunInfo.MaximumSize = new System.Drawing.Size(360, 0);
            this.lblSunInfo.Text = "Today's sunrise/sunset: -";

            // chkFury
            this.chkFury.AutoSize = true;
            this.chkFury.Location = new System.Drawing.Point(12, 145);
            this.chkFury.Text = "Control Kingston FURY DIMM lighting (via FURY CTRL's service)";

            // chkMystic
            this.chkMystic.AutoSize = true;
            this.chkMystic.Location = new System.Drawing.Point(12, 170);
            this.chkMystic.Text = "Control MSI motherboard RGB (Mystic Light SDK)";

            // chkSilenceVolume
            this.chkSilenceVolume.AutoSize = true;
            this.chkSilenceVolume.Location = new System.Drawing.Point(12, 195);
            this.chkSilenceVolume.Text = "Silence system volume at night (unmuted again at sunrise)";

            // chkRunAtStartup
            this.chkRunAtStartup.AutoSize = true;
            this.chkRunAtStartup.Location = new System.Drawing.Point(12, 220);
            this.chkRunAtStartup.Text = "Start with Windows";

            // lblPollInterval
            this.lblPollInterval.AutoSize = true;
            this.lblPollInterval.Location = new System.Drawing.Point(12, 253);
            this.lblPollInterval.Text = "Check every (seconds):";

            // numPollInterval
            this.numPollInterval.Location = new System.Drawing.Point(160, 250);
            this.numPollInterval.Minimum = 15;
            this.numPollInterval.Maximum = 3600;
            this.numPollInterval.Size = new System.Drawing.Size(70, 20);

            // btnOk
            this.btnOk.Text = "OK";
            this.btnOk.Location = new System.Drawing.Point(216, 290);
            this.btnOk.Size = new System.Drawing.Size(80, 27);
            this.btnOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);

            // btnCancel
            this.btnCancel.Text = "Cancel";
            this.btnCancel.Location = new System.Drawing.Point(302, 290);
            this.btnCancel.Size = new System.Drawing.Size(80, 27);
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;

            // SettingsForm
            this.AcceptButton = this.btnOk;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(394, 329);
            this.Controls.Add(this.lblHint);
            this.Controls.Add(this.lblLatitude);
            this.Controls.Add(this.numLatitude);
            this.Controls.Add(this.lblLongitude);
            this.Controls.Add(this.numLongitude);
            this.Controls.Add(this.lblSunInfo);
            this.Controls.Add(this.chkFury);
            this.Controls.Add(this.chkMystic);
            this.Controls.Add(this.chkSilenceVolume);
            this.Controls.Add(this.chkRunAtStartup);
            this.Controls.Add(this.lblPollInterval);
            this.Controls.Add(this.numPollInterval);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "NightLights Settings";
            ((System.ComponentModel.ISupportInitialize)(this.numLatitude)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLongitude)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPollInterval)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
