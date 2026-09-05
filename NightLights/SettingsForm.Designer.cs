using System.Drawing;
using System.Windows.Forms;

namespace NightLights
{
    partial class SettingsForm
    {
        private NumericUpDown numLatitude, numLongitude, numPollInterval, numOpenRgbPort;
        private Label lblSunInfo;
        private TextBox txtOpenRgbStatus;
        private CheckBox chkFury, chkMystic, chkOpenRgb, chkSilenceVolume, chkRunAtStartup, chkPowerSaver;
        private ComboBox cmbSchedule;
        private DateTimePicker timeQuietStart, timeQuietEnd;
        private TextBox txtOpenRgbHost;
        private Button btnProbeOpenRgb;

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 458);
            Text = "NightLights Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Location = new Point(12, 12), Size = new Size(536, 390) };
            var schedule = new TabPage("Night schedule");
            var lighting = new TabPage("Lighting modules");
            var energy = new TabPage("Energy and startup");
            tabs.TabPages.AddRange(new[] { schedule, lighting, energy });
            Controls.Add(tabs);

            AddLabel(schedule, "Choose when night mode turns lighting off and applies enabled modules.", 16, 18, 485);
            AddLabel(schedule, "Automatic night mode:", 16, 60);
            cmbSchedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(165, 56), Size = new Size(340, 24) };
            cmbSchedule.Items.AddRange(new object[] { "Sunset to sunrise", "Quiet hours", "Sunset to sunrise or quiet hours" });
            cmbSchedule.SelectedIndexChanged += (s, e) => UpdateEnabledControls();
            schedule.Controls.Add(cmbSchedule);
            AddLabel(schedule, "Quiet hours (local time):", 16, 102);
            timeQuietStart = AddTimePicker(schedule, 165, 98);
            AddLabel(schedule, "to", 276, 102);
            timeQuietEnd = AddTimePicker(schedule, 308, 98);
            AddLabel(schedule, "Hours can cross midnight. The tray's Force night / day overrides this schedule.", 16, 136, 485);
            AddLabel(schedule, "Location for offline sunrise and sunset calculation", 16, 188, 485);
            AddLabel(schedule, "Latitude:", 16, 223);
            numLatitude = AddNumber(schedule, 95, 219, -90, 90, 115);
            numLatitude.DecimalPlaces = 5;
            AddLabel(schedule, "Longitude:", 257, 223);
            numLongitude = AddNumber(schedule, 337, 219, -180, 180, 115);
            numLongitude.DecimalPlaces = 5;
            numLatitude.ValueChanged += CoordinatesChanged;
            numLongitude.ValueChanged += CoordinatesChanged;
            lblSunInfo = AddLabel(schedule, "Today's sunrise/sunset: -", 16, 262, 485);
            AddLabel(schedule, "Sunset to sunrise or quiet hours keeps lights off whenever either period is active.", 16, 300, 485);

            chkFury = AddCheck(lighting, "Kingston FURY DIMMs (FURY CTRL service)", 16, 18);
            chkMystic = AddCheck(lighting, "MSI motherboard RGB (Mystic Light SDK)", 16, 48);
            chkOpenRgb = AddCheck(lighting, "OpenRGB devices (SDK server)", 16, 78);
            chkOpenRgb.CheckedChanged += (s, e) => UpdateEnabledControls();
            AddLabel(lighting, "Server:", 32, 121);
            txtOpenRgbHost = new TextBox { Location = new Point(98, 117), Size = new Size(230, 22), MaxLength = 253 };
            lighting.Controls.Add(txtOpenRgbHost);
            AddLabel(lighting, "Port:", 343, 121);
            numOpenRgbPort = AddNumber(lighting, 384, 117, 1, 65535, 105);
            btnProbeOpenRgb = new Button { Text = "Test connection / list devices", Location = new Point(32, 156), Size = new Size(245, 28) };
            btnProbeOpenRgb.Click += async (s, e) => await ProbeOpenRgbAsync();
            lighting.Controls.Add(btnProbeOpenRgb);
            txtOpenRgbStatus = new TextBox
            {
                Text = "Start the SDK server in OpenRGB before testing the connection.",
                Location = new Point(32, 199), Size = new Size(457, 72),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
            };
            lighting.Controls.Add(txtOpenRgbStatus);
            AddLabel(lighting, "OpenRGB controls all compatible devices reported by that server. Disable FURY or MSI here if OpenRGB also controls the same hardware.", 16, 280, 485);
            AddLabel(lighting, "Use the tray's day profile commands to save or set colors and brightness.", 16, 329, 485);

            chkPowerSaver = AddCheck(energy, "Use Windows Power saver during night mode", 16, 18);
            AddLabel(energy, "Remembers your current power plan and restores it when night mode ends, when disabled here, or when NightLights exits. Requires an available Power saver plan.", 32, 51, 465);
            AddLabel(energy, "Uses the plan's existing display and sleep timeouts. NightLights does not request immediate sleep or wake the PC at sunrise.", 32, 111, 465);
            chkSilenceVolume = AddCheck(energy, "Mute system volume at night; unmute when night mode ends", 16, 174);
            AddLabel(energy, "Audio changes once per transition, so you can still unmute manually at night.", 32, 207, 465);
            chkRunAtStartup = AddCheck(energy, "Start with Windows", 16, 252);
            AddLabel(energy, "Check every (seconds):", 16, 301);
            numPollInterval = AddNumber(energy, 175, 297, 15, 3600, 85);

            var ok = new Button { Text = "Save", Location = new Point(372, 416), Size = new Size(82, 28) };
            ok.Click += BtnOk_Click;
            var cancel = new Button { Text = "Cancel", Location = new Point(466, 416), Size = new Size(82, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ResumeLayout(false);
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width = 0)
        {
            var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
            if (width > 0) label.MaximumSize = new Size(width, 0);
            parent.Controls.Add(label);
            return label;
        }

        private static CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            var check = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true };
            parent.Controls.Add(check);
            return check;
        }

        private static NumericUpDown AddNumber(Control parent, int x, int y, int min, int max, int width)
        {
            var number = new NumericUpDown { Minimum = min, Maximum = max, Location = new Point(x, y), Size = new Size(width, 22) };
            parent.Controls.Add(number);
            return number;
        }

        private static DateTimePicker AddTimePicker(Control parent, int x, int y)
        {
            var picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(x, y), Size = new Size(95, 22) };
            parent.Controls.Add(picker);
            return picker;
        }
    }
}
