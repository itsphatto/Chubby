using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chubby
{
    public class PetForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly System.Windows.Forms.Timer _moveTimer = new() { Interval = 30 };
        private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 150 };
        private readonly System.Windows.Forms.Timer _topmostTimer = new() { Interval = 1000 };
        private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 40 };
        private readonly System.Windows.Forms.Timer _lingerTimer = new() { Interval = 600 };
        private readonly System.Windows.Forms.Timer _directionTimer = new() { Interval = 15000 };
        private readonly System.Windows.Forms.Timer _fallTimer = new() { Interval = 4 };
        private readonly System.Windows.Forms.Timer _heartTimer = new() { Interval = 30 };
        private readonly System.Windows.Forms.Timer _dustTimer = new() { Interval = 30 };
        private readonly System.Windows.Forms.Timer _greetingTimer = new() { Interval = 4000 };
        private readonly System.Windows.Forms.Timer _sleepAnimTimer = new() { Interval = 600 };
        private readonly Random _random = new();

        private const int PetSize = 90;
        private const int TopMargin = 56;
        private const int WindowHeight = PetSize + TopMargin;
        private const float Gravity = 0.6f;

        private static readonly Color TransparentKey = Color.Magenta;

        private int _x;
        private int _y;
        private int _direction = 1;
        private int _speed = 2;

        private enum PetState { Walking, Idle, Sleeping }

        private PetState _state = PetState.Walking;
        private int _stateTicks = WalkTicks;
        private const int WalkTicks = 233;  // 7s at 30ms/tick
        private const int IdleTicks = 333;  // 10s at 30ms/tick

        // Idle animation pause configuration
        private int _idlePauseTicks = 0;
        private const int IdleAnimationPauseDuration = 20; // Pause ticks between idle runs (~3 seconds delay)

        private int _frameIndex;
        private int _sleepFrameIndex;
        private bool _clickThrough;
        private bool _isHoveringLive;
        private bool _isDragging;
        private Point _dragCursorOffset;
        private float _fallY;
        private float _velocityY;
        private string? _greetingText;

        private sealed class HeartParticle
        {
            public float X;
            public float Y;
            public float BaseX;
            public float WobblePhase;
            public float WobbleSpeed;
            public float SpeedY;
            public int Life;
            public int Scale;
            public Color PrimaryColor;
            public Color HighlightColor;
        }

        private sealed class DustParticle
        {
            public float X;
            public float Y;
            public float VelocityX;
            public int Life;
            public int MaxLife;
            public int Size;
        }

        private readonly List<HeartParticle> _hearts = [];
        private readonly object _heartLock = new();
        private readonly List<DustParticle> _dustParticles = [];
        private readonly object _dustLock = new();
        private int _petDistance;
        private Point _lastHoverPos;
        private DateTime _lastPetTime = DateTime.MinValue;

        private const int IdleFrameCount = 11;
        private const int WalkFrameCount = 6;
        private const int SleepFrameCount = 2;
        private readonly Image?[] _idleFrames = new Image?[IdleFrameCount];
        private readonly Image?[] _walkLeftFrames = new Image?[WalkFrameCount];
        private readonly Image?[] _walkRightFrames = new Image?[WalkFrameCount];
        private readonly Image?[] _sleepLeftFrames = new Image?[SleepFrameCount];
        private readonly Image?[] _sleepRightFrames = new Image?[SleepFrameCount];
        private bool _idleLoaded;
        private bool _walkLoaded;
        private bool _sleepLoaded;

        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(PetSize, WindowHeight);
            BackColor = TransparentKey;
            TransparencyKey = TransparentKey;
            DoubleBuffered = true;

            PositionAboveTaskbar();
            LoadSprites();

            _moveTimer.Tick += (_, _) => MovePet();
            
            // Frame animation ticker with idle pause delay logic
            _animTimer.Tick += (_, _) =>
            {
                if (_state == PetState.Idle)
                {
                    if (_idlePauseTicks > 0)
                    {
                        _idlePauseTicks--;
                        Invalidate();
                        return;
                    }

                    _frameIndex++;
                    if (_frameIndex >= IdleFrameCount)
                    {
                        _frameIndex = 0;
                        _idlePauseTicks = IdleAnimationPauseDuration; // Pause before playing next set
                    }
                }
                else
                {
                    _frameIndex++;
                }

                Invalidate();
            };

            _topmostTimer.Tick += (_, _) => ForceTopmost();
            _hoverTimer.Tick += (_, _) => CheckHover();
            _lingerTimer.Tick += (_, _) => EndLinger();
            _directionTimer.Tick += (_, _) => RandomizeDirection();
            _fallTimer.Tick += (_, _) => UpdateFall();
            _heartTimer.Tick += (_, _) => UpdateHearts();
            _dustTimer.Tick += (_, _) => UpdateDust();
            _greetingTimer.Tick += (_, _) =>
            {
                _greetingText = null;
                _greetingTimer.Stop();
                Invalidate();
            };
            _sleepAnimTimer.Tick += (_, _) =>
            {
                _sleepFrameIndex++;
                Invalidate();
            };

            _moveTimer.Start();
            _animTimer.Start();
            _topmostTimer.Start();
            _hoverTimer.Start();
            _directionTimer.Start();

            BuildContextMenu();
            ShowClockGreeting();
        }

        private static uint GetIdleTimeMs()
        {
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            if (!GetLastInputInfo(ref lastInputInfo))
                return 0;

            uint envTicks = (uint)Environment.TickCount;
            return envTicks >= lastInputInfo.dwTime
                ? envTicks - lastInputInfo.dwTime
                : (uint.MaxValue - lastInputInfo.dwTime) + envTicks;
        }

        private void LoadSprites()
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

            _idleLoaded = TryLoadSequence(assetsDir, "CatIdle", IdleFrameCount, _idleFrames);
            _walkLoaded = TryLoadSequence(assetsDir, "CatWalkingLeft", WalkFrameCount, _walkLeftFrames)
                        & TryLoadSequence(assetsDir, "CatWalkingRight", WalkFrameCount, _walkRightFrames);

            _sleepLoaded = TryLoadSequence(assetsDir, "CatSleepingLeft", SleepFrameCount, _sleepLeftFrames)
                         & TryLoadSequence(assetsDir, "CatSleepingRight", SleepFrameCount, _sleepRightFrames);
        }

        private static bool TryLoadSequence(string assetsDir, string prefix, int count, Image?[] dest)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var path = Path.Combine(assetsDir, $"{prefix}{i + 1}.png");
                    if (!File.Exists(path)) return false;
                    dest[i] = LoadCleanBitmap(path);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Bitmap LoadCleanBitmap(string path)
        {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var raw = new Bitmap(fileStream);
            var clean = new Bitmap(raw.Width, raw.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < raw.Height; y++)
            {
                for (int x = 0; x < raw.Width; x++)
                {
                    var p = raw.GetPixel(x, y);
                    if (p.A < 128)
                    {
                        clean.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    }
                    else
                    {
                        clean.SetPixel(x, y, Color.FromArgb(255, p.R, p.G, p.B));
                    }
                }
            }
            return clean;
        }

        private void RandomizeDirection()
        {
            if (_isDragging) return;
            _direction = _random.Next(0, 2) == 0 ? -1 : 1;
        }

        private void CheckHover()
        {
            if (_clickThrough || _isDragging) return;

            int spriteY = WindowHeight - PetSize;
            var spriteScreenRect = new Rectangle(
                Location.X,
                Location.Y + spriteY,
                PetSize,
                PetSize
            );

            Point curPos = Cursor.Position;
            bool hoveredNow = spriteScreenRect.Contains(curPos);

            if (hoveredNow)
            {
                int dx = curPos.X - _lastHoverPos.X;
                int dy = curPos.Y - _lastHoverPos.Y;
                int dist = Math.Abs(dx) + Math.Abs(dy);
                _lastHoverPos = curPos;

                if (dist > 1 && dist < 80)
                {
                    _petDistance += dist;
                    if (_petDistance >= 50 && (DateTime.UtcNow - _lastPetTime).TotalMilliseconds > 220)
                    {
                        _petDistance = 0;
                        _lastPetTime = DateTime.UtcNow;
                        if (_state == PetState.Sleeping)
                        {
                            WakeUp();
                        }
                        SpawnHeart();
                    }
                }
            }
            else
            {
                _lastHoverPos = curPos;
                _petDistance = 0;
            }

            if (hoveredNow && !_isHoveringLive)
                StartHover();
            else if (!hoveredNow && _isHoveringLive)
                StopHover();
        }

        private void GoToSleep()
        {
            _state = PetState.Sleeping;
            _sleepFrameIndex = 0;
            _sleepAnimTimer.Start();
            Invalidate();
        }

        private void WakeUp()
        {
            _state = PetState.Idle;
            _stateTicks = IdleTicks;
            _frameIndex = 0;
            _idlePauseTicks = 0;
            _sleepAnimTimer.Stop();
            if (!_animTimer.Enabled) _animTimer.Start();
            if (!_moveTimer.Enabled) _moveTimer.Start();
            if (!_directionTimer.Enabled) _directionTimer.Start();
            Invalidate();
        }

        private void SpawnHeart(int? clickX = null, int? clickY = null)
        {
            int spriteY = WindowHeight - PetSize;
            float spawnX;
            float spawnY;

            if (clickX.HasValue && clickY.HasValue)
            {
                spawnX = Math.Clamp(clickX.Value - 7, 5, PetSize - 20);
                spawnY = Math.Clamp(clickY.Value - 10, 10, spriteY + 20);
            }
            else
            {
                spawnX = (PetSize / 2f - 7) + _random.Next(-14, 15);
                spawnY = spriteY - _random.Next(2, 12);
            }

            int scale = _random.Next(0, 4) == 0 ? 1 : 2;

            (Color main, Color highlight) = (_random.Next(0, 3)) switch
            {
                0 => (Color.FromArgb(255, 245, 60, 100), Color.FromArgb(255, 255, 175, 195)),
                1 => (Color.FromArgb(255, 230, 40, 70), Color.FromArgb(255, 255, 150, 170)),
                _ => (Color.FromArgb(255, 255, 105, 150), Color.FromArgb(255, 255, 205, 225))
            };

            var heart = new HeartParticle
            {
                X = spawnX,
                Y = spawnY,
                BaseX = spawnX,
                WobblePhase = (float)(_random.NextDouble() * Math.PI * 2),
                WobbleSpeed = (float)(0.12f + _random.NextDouble() * 0.08f),
                SpeedY = (float)(1.2f + _random.NextDouble() * 0.8f),
                Life = _random.Next(28, 42),
                Scale = scale,
                PrimaryColor = main,
                HighlightColor = highlight
            };

            lock (_heartLock)
            {
                _hearts.Add(heart);
            }

            if (!_heartTimer.Enabled)
            {
                _heartTimer.Start();
            }

            Invalidate();
        }

        private void UpdateHearts()
        {
            lock (_heartLock)
            {
                for (int i = _hearts.Count - 1; i >= 0; i--)
                {
                    var h = _hearts[i];
                    h.Y -= h.SpeedY;
                    h.WobblePhase += h.WobbleSpeed;
                    h.X = h.BaseX + (float)Math.Sin(h.WobblePhase) * 6f;
                    h.Life--;

                    if (h.Life <= 0 || h.Y < -15)
                    {
                        _hearts.RemoveAt(i);
                    }
                }

                if (_hearts.Count == 0)
                {
                    _heartTimer.Stop();
                }
            }

            Invalidate();
        }

        private void SpawnDust(int count)
        {
            int spriteY = WindowHeight - PetSize;
            float baseX = PetSize / 2f;
            float baseY = spriteY + PetSize - 4;

            lock (_dustLock)
            {
                for (int i = 0; i < count; i++)
                {
                    _dustParticles.Add(new DustParticle
                    {
                        X = baseX + _random.Next(-14, 15),
                        Y = baseY,
                        VelocityX = (float)(_random.NextDouble() * 1.6 - 0.8),
                        Life = 0,
                        MaxLife = _random.Next(14, 22),
                        Size = _random.Next(3, 6)
                    });
                }
            }

            if (!_dustTimer.Enabled) _dustTimer.Start();
            Invalidate();
        }

        private void UpdateDust()
        {
            lock (_dustLock)
            {
                for (int i = _dustParticles.Count - 1; i >= 0; i--)
                {
                    var d = _dustParticles[i];
                    d.X += d.VelocityX;
                    d.Y -= 0.3f;
                    d.Life++;

                    if (d.Life >= d.MaxLife)
                        _dustParticles.RemoveAt(i);
                }

                if (_dustParticles.Count == 0) _dustTimer.Stop();
            }

            Invalidate();
        }

        private void ShowClockGreeting()
        {
            int hour = DateTime.Now.Hour;
            _greetingText = hour switch
            {
                >= 5 and < 12 => "Good morning!",
                >= 12 and < 17 => "Good afternoon!",
                >= 17 and < 22 => "Good evening!",
                _ => "It's late, go to sleep!"
            };

            _greetingTimer.Stop();
            _greetingTimer.Start();
            Invalidate();
        }

        private void StartHover()
        {
            _isHoveringLive = true;
            _lingerTimer.Stop();
            _moveTimer.Stop();
            _animTimer.Stop();
            _directionTimer.Stop();

            Invalidate();
        }

        private void StopHover()
        {
            _isHoveringLive = false;
            _lingerTimer.Start();
        }

        private void EndLinger()
        {
            _lingerTimer.Stop();
            _frameIndex = 0;
            _idlePauseTicks = 0;
            _moveTimer.Start();
            _animTimer.Start();
            _directionTimer.Start();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left || _clickThrough) return;

            if (_state == PetState.Sleeping)
            {
                WakeUp();
            }

            SpawnHeart(e.X, e.Y);

            _isDragging = true;
            _dragCursorOffset = e.Location;

            _moveTimer.Stop();
            _animTimer.Stop();
            _hoverTimer.Stop();
            _lingerTimer.Stop();
            _directionTimer.Stop();
            _fallTimer.Stop();

            _isHoveringLive = false;

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isDragging) return;

            var screenBounds = Screen.FromPoint(Cursor.Position).WorkingArea;
            var cursor = PointToScreen(e.Location);

            int newX = Math.Clamp(
                cursor.X - _dragCursorOffset.X,
                screenBounds.Left,
                screenBounds.Right - PetSize
            );

            int newY = Math.Clamp(
                cursor.Y - _dragCursorOffset.Y,
                screenBounds.Top,
                screenBounds.Bottom - WindowHeight
            );

            _x = newX;
            _y = newY;
            Location = new Point(newX, newY);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left || !_isDragging) return;

            _isDragging = false;
            StartFalling();
        }

        private void StartFalling()
        {
            var workArea = Screen.FromPoint(Location).WorkingArea;
            int floorY = workArea.Bottom - WindowHeight;

            if (_y >= floorY)
            {
                Land();
                return;
            }

            _fallY = _y;
            _velocityY = 0;
            _fallTimer.Start();
        }

        private void UpdateFall()
        {
            var workArea = Screen.FromPoint(Location).WorkingArea;
            int floorY = workArea.Bottom - WindowHeight;

            _velocityY += Gravity;
            _fallY += _velocityY;

            if (_fallY >= floorY)
            {
                _fallTimer.Stop();
                _y = floorY;
                Location = new Point(_x, _y);
                Land();
                return;
            }

            _y = (int)Math.Round(_fallY);
            Location = new Point(_x, _y);
        }

        private void Land()
        {
            var workArea = Screen.FromPoint(Location).WorkingArea;
            _y = workArea.Bottom - WindowHeight;
            Location = new Point(_x, _y);

            SpawnDust(5);

            _moveTimer.Start();
            _animTimer.Start();
            _hoverTimer.Start();
            _directionTimer.Start();
        }

        private void PositionAboveTaskbar()
        {
            var workArea = Screen.PrimaryScreen!.WorkingArea;
            _x = workArea.Left + 40;
            _y = workArea.Bottom - WindowHeight;
            Location = new Point(_x, _y);
        }

        private void MovePet()
        {
            // System Inactivity check: sleep if no keyboard/mouse input for 40 seconds (40000ms)
            uint idleMs = GetIdleTimeMs();
            if (idleMs >= 40000)
            {
                if (_state != PetState.Sleeping)
                {
                    GoToSleep();
                }
                Invalidate();
                return;
            }
            else if (_state == PetState.Sleeping)
            {
                WakeUp();
            }

            // Normal active cycle between Walking and Idle
            if (--_stateTicks <= 0)
            {
                _state = _state switch
                {
                    PetState.Walking => PetState.Idle,
                    _ => PetState.Walking
                };

                _stateTicks = _state switch
                {
                    PetState.Walking => WalkTicks,
                    _ => IdleTicks
                };

                _frameIndex = 0;
                _idlePauseTicks = 0;

                if (_state == PetState.Walking) SpawnDust(3);
            }

            if (_state != PetState.Walking)
            {
                Invalidate();
                return;
            }

            var workArea = Screen.PrimaryScreen!.WorkingArea;
            _x += _speed * _direction;

            if (_x <= workArea.Left)
            {
                _x = workArea.Left;
                _direction = 1;
            }
            else if (_x + PetSize >= workArea.Right)
            {
                _x = workArea.Right - PetSize;
                _direction = -1;
            }

            _y = workArea.Bottom - WindowHeight;
            Location = new Point(_x, _y);
        }

        private void ForceTopmost()
        {
            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
            );
        }

        private void ToggleClickThrough()
        {
            _clickThrough = !_clickThrough;

            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED;

            exStyle = _clickThrough
                ? exStyle | WS_EX_TRANSPARENT
                : exStyle & ~WS_EX_TRANSPARENT;

            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

            if (_clickThrough)
            {
                _isHoveringLive = false;
                _lingerTimer.Stop();

                if (!_moveTimer.Enabled) _moveTimer.Start();
                if (!_animTimer.Enabled) _animTimer.Start();
                if (!_directionTimer.Enabled) _directionTimer.Start();

                Invalidate();
            }
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var clickThroughItem = new ToolStripMenuItem("Click-through")
            {
                CheckOnClick = true
            };

            clickThroughItem.Click += (_, _) => ToggleClickThrough();
            menu.Items.Add(clickThroughItem);

            menu.Items.Add(new ToolStripSeparator());

            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += (_, _) => Close();
            menu.Items.Add(removeItem);

            ContextMenuStrip = menu;

            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _moveTimer.Stop();
            _animTimer.Stop();
            _topmostTimer.Stop();
            _hoverTimer.Stop();
            _lingerTimer.Stop();
            _directionTimer.Stop();
            _fallTimer.Stop();
            _heartTimer.Stop();
            _dustTimer.Stop();
            _greetingTimer.Stop();
            _sleepAnimTimer.Stop();

            _moveTimer.Dispose();
            _animTimer.Dispose();
            _topmostTimer.Dispose();
            _hoverTimer.Dispose();
            _lingerTimer.Dispose();
            _directionTimer.Dispose();
            _fallTimer.Dispose();
            _heartTimer.Dispose();
            _dustTimer.Dispose();
            _greetingTimer.Dispose();
            _sleepAnimTimer.Dispose();

            foreach (var f in _idleFrames) f?.Dispose();
            foreach (var f in _walkLeftFrames) f?.Dispose();
            foreach (var f in _walkRightFrames) f?.Dispose();
            foreach (var f in _sleepLeftFrames) f?.Dispose();
            foreach (var f in _sleepRightFrames) f?.Dispose();

            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool facingLeft = _direction < 0;
            int spriteY = WindowHeight - PetSize;

            bool isFrozenWalking = (_isHoveringLive || _lingerTimer.Enabled || _isDragging) && _state == PetState.Walking;

            Image? frame = _state switch
            {
                PetState.Walking when isFrozenWalking && _idleLoaded => _idleFrames[0],
                PetState.Walking when _walkLoaded => facingLeft
                    ? _walkLeftFrames[_frameIndex % WalkFrameCount]
                    : _walkRightFrames[_frameIndex % WalkFrameCount],
                PetState.Sleeping when _sleepLoaded => facingLeft
                    ? _sleepLeftFrames[_sleepFrameIndex % SleepFrameCount]
                    : _sleepRightFrames[_sleepFrameIndex % SleepFrameCount],
                PetState.Idle when _idleLoaded => _idleFrames[_frameIndex % IdleFrameCount],
                _ => null
            };

            if (frame != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                bool usesIdleOrientation = _state == PetState.Idle || isFrozenWalking;
                if (usesIdleOrientation && facingLeft)
                {
                    var savedState = g.Save();
                    g.TranslateTransform(PetSize, 0);
                    g.ScaleTransform(-1, 1);
                    g.DrawImage(frame, 0, spriteY, PetSize, PetSize);
                    g.Restore(savedState);
                }
                else
                {
                    g.DrawImage(frame, 0, spriteY, PetSize, PetSize);
                }
            }
            else
            {
                g.SmoothingMode = SmoothingMode.None;
                int legOffset = _state == PetState.Walking && _frameIndex % 2 == 0 ? 4 : -4;

                using var bodyBrush = new SolidBrush(Color.FromArgb(255, 120, 170, 240));
                using var eyeBrush = new SolidBrush(Color.Black);
                using var legPen = new Pen(Color.FromArgb(255, 90, 130, 190), 5);

                g.FillEllipse(bodyBrush, 6, spriteY + 10, PetSize - 12, PetSize - 20);

                g.DrawLine(
                    legPen,
                    16, spriteY + PetSize - 14,
                    16 + legOffset, spriteY + PetSize - 2
                );

                g.DrawLine(
                    legPen,
                    PetSize - 16, spriteY + PetSize - 14,
                    PetSize - 16 - legOffset, spriteY + PetSize - 2
                );

                int eyeX = facingLeft ? 12 : PetSize - 18;
                g.FillEllipse(eyeBrush, eyeX, spriteY + 16, 6, 6);
            }

            lock (_heartLock)
            {
                if (_hearts.Count > 0)
                {
                    foreach (var heart in _hearts)
                    {
                        DrawPixelHeart(g, heart);
                    }
                }
            }

            if (_greetingText != null)
            {
                using var font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
                float bw = PetSize - 4f;
                float bh = TopMargin - 6f;
                float br = 8f;

                using var bubblePath = new GraphicsPath();
                bubblePath.AddArc(2, 2, br, br, 180, 90);
                bubblePath.AddArc(2 + bw - br, 2, br, br, 270, 90);
                bubblePath.AddArc(2 + bw - br, 2 + bh - br, br, br, 0, 90);
                bubblePath.AddArc(2, 2 + bh - br, br, br, 90, 90);
                bubblePath.CloseFigure();

                using var bubbleBrush = new SolidBrush(Color.FromArgb(255, 255, 250, 235));
                using var bubbleBorder = new Pen(Color.FromArgb(255, 210, 195, 160), 1);
                g.SmoothingMode = SmoothingMode.None;
                g.FillPath(bubbleBrush, bubblePath);
                g.DrawPath(bubbleBorder, bubblePath);

                using var textBrush = new SolidBrush(Color.FromArgb(255, 60, 50, 40));
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(_greetingText, font, textBrush, new RectangleF(3, 3, bw - 2, bh - 4), format);
            }
        }

        private static void DrawPixelHeart(Graphics g, HeartParticle heart)
        {
            int s = heart.Scale;
            int hx = (int)Math.Round(heart.X);
            int hy = (int)Math.Round(heart.Y);

            using var mainBrush = new SolidBrush(heart.PrimaryColor);
            using var highBrush = new SolidBrush(heart.HighlightColor);

            g.FillRectangle(mainBrush, hx + 1 * s, hy + 0 * s, 2 * s, s);
            g.FillRectangle(mainBrush, hx + 4 * s, hy + 0 * s, 2 * s, s);
            g.FillRectangle(mainBrush, hx + 0 * s, hy + 1 * s, 7 * s, s);
            g.FillRectangle(mainBrush, hx + 0 * s, hy + 2 * s, 7 * s, s);
            g.FillRectangle(mainBrush, hx + 1 * s, hy + 3 * s, 5 * s, s);
            g.FillRectangle(mainBrush, hx + 2 * s, hy + 4 * s, 3 * s, s);
            g.FillRectangle(mainBrush, hx + 3 * s, hy + 5 * s, 1 * s, s);

            g.FillRectangle(highBrush, hx + 1 * s, hy + 1 * s, s, s);
        }
    }
}