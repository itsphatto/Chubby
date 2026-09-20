using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskbarPet
{
    public class ControllerForm : Form
    {
        private static readonly Color BgColor = Color.White;
        private static readonly Color AccentColor = Color.FromArgb(76, 130, 246); 
        private static readonly Color AccentHover = Color.FromArgb(60, 110, 220);
        private static readonly Color TextDark = Color.FromArgb(30, 32, 38);
        private static readonly Color TextMuted = Color.FromArgb(130, 134, 142);
        private static readonly Color BorderColor = Color.FromArgb(220, 222, 228);

        private PetForm? _pet;

        private readonly Label _title = new()
        {
            Text = "Chubby",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        };

        private readonly Label _subtitle = new()
        {
            Text = "your only friend on the taskbar",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        private readonly Panel _statusDot = new()
        {
            Size = new Size(10, 10),
            BackColor = Color.FromArgb(200, 204, 210)
        };

        private readonly Label _status = new()
        {
            Text = "Stopped",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = TextMuted
        };

        private readonly Button _spawnBtn = new()
        {
            Text = "Spawn Chubby",
            Dock = DockStyle.Top,
            Height = 48,
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };

        private readonly Button _removeBtn = new()
        {
            Text = "Remove",
            Dock = DockStyle.Top,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = BgColor,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 10.5F),
            Enabled = false,
            Cursor = Cursors.Hand
        };

        public ControllerForm()
        {
            Text = "Chubby";
            ClientSize = new Size(320, 300);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;

            _spawnBtn.FlatAppearance.BorderSize = 0;
            _spawnBtn.FlatAppearance.MouseOverBackColor = AccentHover;
            _spawnBtn.Click += (_, _) => SpawnPet();

            _removeBtn.FlatAppearance.BorderSize = 1;
            _removeBtn.FlatAppearance.BorderColor = BorderColor;
            _removeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 249);
            _removeBtn.Click += (_, _) => RemovePet();

            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 24, 28, 28),
                BackColor = BgColor
            };

            var statusRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36
            };
            _statusDot.Location = new Point(2, 12);
            _status.Location = new Point(20, 9);
            statusRow.Controls.Add(_statusDot);
            statusRow.Controls.Add(_status);

            var spacerSmall = new Panel { Dock = DockStyle.Top, Height = 8 };
            var spacerMed = new Panel { Dock = DockStyle.Top, Height = 20 };
            var spacerBetweenBtns = new Panel { Dock = DockStyle.Top, Height = 10 };

            root.Controls.Add(_removeBtn);
            root.Controls.Add(spacerBetweenBtns);
            root.Controls.Add(_spawnBtn);
            root.Controls.Add(spacerMed);
            root.Controls.Add(statusRow);
            root.Controls.Add(spacerSmall);
            root.Controls.Add(_subtitle);
            root.Controls.Add(_title);

            Controls.Add(root);

            ApplyRoundedCorners(_spawnBtn, 10);
            ApplyRoundedCorners(_removeBtn, 10);
        }

        private static void ApplyRoundedCorners(Control control, int radius)
        {
            control.Resize += (_, _) => control.Region = RoundedRegion(control.Width, control.Height, radius);
            control.Region = RoundedRegion(control.Width, control.Height, radius);
        }

        private static Region RoundedRegion(int width, int height, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(width - d, 0, d, d, 270, 90);
            path.AddArc(width - d, height - d, d, d, 0, 90);
            path.AddArc(0, height - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        private void SpawnPet()
        {
            if (_pet != null) return;

            _pet = new PetForm();
            _pet.FormClosed += (_, _) =>
            {
                _pet = null;
                UpdateState();
            };
            _pet.Show();
            UpdateState();
        }

        private void RemovePet()
        {
            _pet?.Close();
            _pet = null;
            UpdateState();
        }

        private void UpdateState()
        {
            bool running = _pet != null;
            _status.Text = running ? "Running" : "Stopped";
            _statusDot.BackColor = running
                ? Color.FromArgb(74, 200, 130)  
                : Color.FromArgb(200, 204, 210); 
            _spawnBtn.Enabled = !running;
            _spawnBtn.BackColor = running ? Color.FromArgb(230, 232, 236) : AccentColor;
            _removeBtn.Enabled = running;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _pet?.Close();
            base.OnFormClosing(e);
        }
    }
}