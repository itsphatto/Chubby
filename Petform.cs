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
        private readonly Random _random = new();

        private const int PetSize = 90;
        private const int BubbleAreaHeight = 56;
        private const int WindowHeight = PetSize + BubbleAreaHeight;
        private const float Gravity = 0.6f;

        private int _x;
        private int _y;
        private int _direction = 1;
        private int _speed = 2;

        private enum PetState { Walking, Idle, Sleeping }

        // Behavior cycle: walk, then idle for a bit, then sleep, then walk again.
        private PetState _state = PetState.Walking;
        private int _stateTicks = WalkTicks;
        private const int WalkTicks = 233;  // 7s at 30ms/tick (_moveTimer)
        private const int IdleTicks = 333;  // 10s
        private const int SleepTicks = 1000; // 30s

        private int _frameIndex;
        private bool _clickThrough;
        private bool _isHoveringLive;
        private bool _bubbleVisible;
        private bool _bubbleShownOnce;
        private bool _isDragging;
        private Point _dragCursorOffset;
        private float _fallY;
        private float _velocityY;

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

        private readonly List<HeartParticle> _hearts = [];
        private readonly object _heartLock = new();
        private int _petDistance;
        private Point _lastHoverPos;
        private DateTime _lastPetTime = DateTime.MinValue;

        private const int IdleFrameCount = 11;
        private const int WalkFrameCount = 6;
        private readonly Image?[] _idleFrames = new Image?[IdleFrameCount];
        private readonly Image?[] _walkLeftFrames = new Image?[WalkFrameCount];
        private readonly Image?[] _walkRightFrames = new Image?[WalkFrameCount];
        private Image? _sleepLeft;
        private Image? _sleepRight;
        private bool _idleLoaded;
        private bool _walkLoaded;
        private bool _sleepLoaded;
        private Image? _bubble;
        private bool _bubbleLoaded;

        public PetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(PetSize, WindowHeight);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            PositionAboveTaskbar();
            LoadSprites();

            _moveTimer.Tick += (_, _) => MovePet();
            _animTimer.Tick += (_, _) =>
            {
                _frameIndex++;
                Invalidate();
            };

            _topmostTimer.Tick += (_, _) => ForceTopmost();
            _hoverTimer.Tick += (_, _) => CheckHover();
            _lingerTimer.Tick += (_, _) => EndLinger();
            _directionTimer.Tick += (_, _) => RandomizeDirection();
            _fallTimer.Tick += (_, _) => UpdateFall();
            _heartTimer.Tick += (_, _) => UpdateHearts();

            _moveTimer.Start();
            _animTimer.Start();
            _topmostTimer.Start();
            _hoverTimer.Start();
            _directionTimer.Start();

            BuildContextMenu();
        }

        private void LoadSprites()
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

            _idleLoaded = TryLoadSequence(assetsDir, "CatIdle", IdleFrameCount, _idleFrames);
            _walkLoaded = TryLoadSequence(assetsDir, "CatWalkingLeft", WalkFrameCount, _walkLeftFrames)
                        & TryLoadSequence(assetsDir, "CatWalkingRight", WalkFrameCount, _walkRightFrames);

            try
            {
                var left = Path.Combine(assetsDir, "CatSleepingLeft.png");
                var right = Path.Combine(assetsDir, "CatSleepingRight.png");

                if (File.Exists(left) && File.Exists(right))
                {
                    _sleepLeft = LoadCleanBitmap(left);
                    _sleepRight = LoadCleanBitmap(right);
                    _sleepLoaded = true;
                }
            }
            catch
            {
                _sleepLoaded = false;
            }

            try
            {
                var bubblePath = Path.Combine(assetsDir, "HiChatBubble.png");

                if (File.Exists(bubblePath))
                {
                    _bubble = LoadCleanBitmap(bubblePath);
                    _bubbleLoaded = true;
                }
            }
            catch
            {
                _bubbleLoaded = false;
            }
        }

        // Loads "<prefix>1.png" .. "<prefix>{count}.png" into dest. Returns true only if every frame loaded.
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

        private void WakeUp()
        {
            _state = PetState.Idle;
            _stateTicks = IdleTicks;
            _frameIndex = 0;
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

            _bubbleVisible = false;
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

        private void StartHover()
        {
            _isHoveringLive = true;
            _lingerTimer.Stop();
            _moveTimer.Stop();
            _animTimer.Stop();
            _directionTimer.Stop();

            if (!_bubbleShownOnce)
            {
                _bubbleVisible = true;
                _bubbleShownOnce = true;
            }

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
            _bubbleVisible = false;
            _frameIndex = 0;
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
            _bubbleVisible = false;

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
            if (--_stateTicks <= 0)
            {
                (_state, _stateTicks) = _state switch
                {
                    PetState.Walking => (PetState.Idle, IdleTicks),
                    PetState.Idle => (PetState.Sleeping, SleepTicks),
                    _ => (PetState.Walking, WalkTicks)
                };
                _frameIndex = 0;
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
                _bubbleVisible = false;

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

            _moveTimer.Dispose();
            _animTimer.Dispose();
            _topmostTimer.Dispose();
            _hoverTimer.Dispose();
            _lingerTimer.Dispose();
            _directionTimer.Dispose();
            _fallTimer.Dispose();
            _heartTimer.Dispose();

            foreach (var f in _idleFrames) f?.Dispose();
            foreach (var f in _walkLeftFrames) f?.Dispose();
            foreach (var f in _walkRightFrames) f?.Dispose();
            _sleepLeft?.Dispose();
            _sleepRight?.Dispose();
            _bubble?.Dispose();

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
                PetState.Sleeping when _sleepLoaded => facingLeft ? _sleepLeft : _sleepRight,
                PetState.Idle when _idleLoaded => _idleFrames[_frameIndex % IdleFrameCount],
                _ => null
            };

            if (frame != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                // Idle art is drawn facing right; flip it for left-facing idle.
                // Walk/sleep sets already have dedicated left/right art, so no flip needed.
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
                g.SmoothingMode = SmoothingMode.AntiAlias;
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

            if (_bubbleVisible && _bubbleLoaded && _bubble != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                float scale = Math.Min(
                    (float)PetSize / _bubble.Width,
                    (float)BubbleAreaHeight / _bubble.Height
                );

                int drawW = (int)(_bubble.Width * scale);
                int drawH = (int)(_bubble.Height * scale);
                int drawX = (PetSize - drawW) / 2;
                int drawY = spriteY - drawH;

                g.DrawImage(_bubble, drawX, drawY, drawW, drawH);
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
        }

        private static void DrawPixelHeart(Graphics g, HeartParticle heart)
        {
            int s = heart.Scale;
            int hx = (int)Math.Round(heart.X);
            int hy = (int)Math.Round(heart.Y);

            using var mainBrush = new SolidBrush(heart.PrimaryColor);
            using var highBrush = new SolidBrush(heart.HighlightColor);

            // Row 0: . ## . ## .
            g.FillRectangle(mainBrush, hx + 1 * s, hy + 0 * s, 2 * s, s);
            g.FillRectangle(mainBrush, hx + 4 * s, hy + 0 * s, 2 * s, s);

            // Row 1: #######
            g.FillRectangle(mainBrush, hx + 0 * s, hy + 1 * s, 7 * s, s);

            // Row 2: #######
            g.FillRectangle(mainBrush, hx + 0 * s, hy + 2 * s, 7 * s, s);

            // Row 3: . ##### .
            g.FillRectangle(mainBrush, hx + 1 * s, hy + 3 * s, 5 * s, s);

            // Row 4: .. ### ..
            g.FillRectangle(mainBrush, hx + 2 * s, hy + 4 * s, 3 * s, s);

            // Row 5: ... # ...
            g.FillRectangle(mainBrush, hx + 3 * s, hy + 5 * s, 1 * s, s);

            // Cute pixel sparkle / highlight on upper left
            g.FillRectangle(highBrush, hx + 1 * s, hy + 1 * s, s, s);
        }
    }
}