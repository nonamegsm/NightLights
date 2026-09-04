using System;
using System.Drawing;
using System.Windows.Forms;

namespace NightLights
{
    /// <summary>
    /// Minimal "pick a day-profile color + brightness" dialog, launched from the tray menu's
    /// "Set day profile color...". Written as one plain code-behind file (no Designer split) -
    /// it's just a swatch, a "Choose..." button, a brightness slider and OK/Cancel, not worth
    /// wiring up in the WinForms designer.
    /// </summary>
    internal sealed class DayProfileColorForm : Form
    {
        public Color Color { get; private set; }
        public int BrightnessPercent { get; private set; }

        private readonly Panel _swatch;
        private readonly TrackBar _brightnessTrack;
        private readonly Label _brightnessValueLabel;

        public DayProfileColorForm(Color initialColor, int initialBrightnessPercent)
        {
            Color = initialColor;
            BrightnessPercent = Math.Max(0, Math.Min(100, initialBrightnessPercent));

            Text = "Set Day Profile Color";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(300, 172);

            var colorLabel = new Label { Text = "Color:", Location = new Point(12, 18), AutoSize = true };

            _swatch = new Panel
            {
                BackColor = Color,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(70, 12),
                Size = new Size(40, 24)
            };

            var pickButton = new Button { Text = "Choose...", Location = new Point(120, 11), Size = new Size(90, 26) };
            pickButton.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = Color })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        Color = dlg.Color;
                        _swatch.BackColor = Color;
                    }
                }
            };

            var brightnessCaption = new Label { Text = "Brightness:", Location = new Point(12, 58), AutoSize = true };

            _brightnessTrack = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = BrightnessPercent,
                Location = new Point(12, 78),
                Size = new Size(200, 45)
            };
            _brightnessValueLabel = new Label
            {
                Text = BrightnessPercent + "%",
                Location = new Point(220, 88),
                AutoSize = true
            };
            _brightnessTrack.ValueChanged += (s, e) =>
            {
                BrightnessPercent = _brightnessTrack.Value;
                _brightnessValueLabel.Text = BrightnessPercent + "%";
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(112, 132),
                Size = new Size(80, 27)
            };
            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(198, 132),
                Size = new Size(80, 27)
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(colorLabel);
            Controls.Add(_swatch);
            Controls.Add(pickButton);
            Controls.Add(brightnessCaption);
            Controls.Add(_brightnessTrack);
            Controls.Add(_brightnessValueLabel);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
        }
    }
}
