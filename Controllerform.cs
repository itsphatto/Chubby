using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Chubby
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
        private readonly NotifyIcon _trayIcon = new();
        private ToolStripMenuItem? _trayTogglePetItem;
        private bool _isExiting;
        private bool _balloonShown;

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
            Text = "Your Working Friend!",
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

        private readonly Label _footer = new()
        {
            Text = "github/itsphatto/Chubby",
            Dock = DockStyle.Bottom,
            Height = 24,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };

        public ControllerForm()
        {
            Text = "Chubby";
            ClientSize = new Size(320, 330);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;

            // Set window title bar & taskbar icon
            Icon = LoadTrayIcon();

            _spawnBtn.FlatAppearance.BorderSize = 0;
            _spawnBtn.FlatAppearance.MouseOverBackColor = AccentHover;
            _spawnBtn.Click += (_, _) => SpawnPet();

            _removeBtn.FlatAppearance.BorderSize = 1;
            _removeBtn.FlatAppearance.BorderColor = BorderColor;
            _removeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 247, 249);
            _removeBtn.Click += (_, _) => RemovePet();

            _footer.Click += (_, _) => OpenGitHubRepository();

            BuildLayout();
            BuildTrayIcon();
        }

        private static void OpenGitHubRepository()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/itsphatto/Chubby",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback if browser fails to launch
            }
        }

        private void BuildTrayIcon()
        {
            _trayIcon.Text = "Chubby - Desktop Pet";
            _trayIcon.Icon = LoadTrayIcon();
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Controller", null, (_, _) => RestoreFromTray())
            {
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            menu.Items.Add(openItem);

            _trayTogglePetItem = new ToolStripMenuItem("Spawn Chubby", null, (_, _) =>
            {
                if (_pet == null) SpawnPet();
                else RemovePet();
            });
            menu.Items.Add(_trayTogglePetItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                var idlePath = Path.Combine(assetsDir, "CatIdle1.png");
                if (File.Exists(idlePath))
                {
                    using var stream = new FileStream(idlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var raw = new Bitmap(stream);
                    using var square = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(square))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(raw, 2, 0, 28, 32);
                    }
                    IntPtr hIcon = square.GetHicon();
                    Icon icon = Icon.FromHandle(hIcon);
                    return (Icon)icon.Clone();
                }
            }
            catch
            {
                // Fallback
            }

            return SystemIcons.Application;
        }

        private void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;

            if (!_balloonShown)
            {
                _balloonShown = true;
                _trayIcon.ShowBalloonTip(2000, "Chubby", "Chubby is still running in your system tray!", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            Close();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                MinimizeToTray();
            }
        }

        private void BuildLayout()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 20, 28, 16),
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
            var spacerMed = new Panel { Dock = DockStyle.Top, Height = 16 };
            var spacerBetweenBtns = new Panel { Dock = DockStyle.Top, Height = 10 };

            root.Controls.Add(_footer);
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

            if (_trayTogglePetItem != null)
            {
                _trayTogglePetItem.Text = running ? "Remove Chubby" : "Spawn Chubby";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _pet?.Close();
            base.OnFormClosing(e);
        }
    }
}