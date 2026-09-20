using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarPet
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
        private readonly System.Windows.Forms.Timer _lingerTimer = new() { Interval = 2000 };
        private readonly System.Windows.Forms.Timer _flightTimer = new() { Interval = 2 };

        private const int PetSize = 64;
        private const int BubbleAreaHeight = 56;
        private const int WindowHeight = PetSize + BubbleAreaHeight;
        private const float Gravity = 0.55f;
        private const float AirResistance = 0.997f;
        private const float WallBounce = 0.2f;
        private const float FloorBounce = 0.62f;
        private const float StopSpeed = 0.45f;

        private int _x;
        private int _y;
        private int _direction = 1;
        private int _speed = 2;
        private bool _walkFrame;
        private bool _clickThrough;
        private bool _isHoveringLive;
        private bool _bubbleVisible;
        private bool _bubbleShownOnce;
        private bool _isDragging;
        private Point _dragCursorOffset;
        private bool _isFlying;
        private float _physicsX;
        private float _physicsY;
        private float _velocityX;
        private float _velocityY;
        private Point _lastDragScreenPos;
        private long _lastDragTime;
        private int _quietGroundFrames;
        private Image? _frame1;
        private Image? _frame2;
        private bool _spritesLoaded;
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
            _animTimer.Tick += (_, _) => { _walkFrame = !_walkFrame; Invalidate(); };
            _topmostTimer.Tick += (_, _) => ForceTopmost();
            _hoverTimer.Tick += (_, _) => CheckHover();
            _lingerTimer.Tick += (_, _) => EndLinger();
            _flightTimer.Tick += (_, _) => UpdateFlight();
            _moveTimer.Start();
            _animTimer.Start();
            _topmostTimer.Start();
            _hoverTimer.Start();
            BuildContextMenu();
        }

        private void LoadSprites()
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
            try
            {
                var path1 = Path.Combine(assetsDir, "ChubbyIdle1.png");
                var path2 = Path.Combine(assetsDir, "ChubbyIdle2.png");
                if (File.Exists(path1) && File.Exists(path2))
                {
                    _frame1 = Image.FromFile(path1);
                    _frame2 = Image.FromFile(path2);
                    _spritesLoaded = true;
                }
            }
            catch { _spritesLoaded = false; }
            try
            {
                var bubblePath = Path.Combine(assetsDir, "HiChatBubble.png");
                if (File.Exists(bubblePath)) { _bubble = Image.FromFile(bubblePath); _bubbleLoaded = true; }
            }
            catch { _bubbleLoaded = false; }
        }

        private void CheckHover()
        {
            if (_clickThrough || _isDragging || _isFlying) return;
            int spriteY = WindowHeight - PetSize;
            var spriteScreenRect = new Rectangle(Location.X, Location.Y + spriteY, PetSize, PetSize);
            bool hoveredNow = spriteScreenRect.Contains(Cursor.Position);
            if (hoveredNow && !_isHoveringLive) StartHover();
            else if (!hoveredNow && _isHoveringLive) StopHover();
        }

        private void StartHover()
        {
            _isHoveringLive = true; _lingerTimer.Stop(); _moveTimer.Stop(); _animTimer.Stop();
            if (!_bubbleShownOnce) { _bubbleVisible = true; _bubbleShownOnce = true; }
            Invalidate();
        }

        private void StopHover() { _isHoveringLive = false; _lingerTimer.Start(); }

        private void EndLinger()
        {
            _lingerTimer.Stop(); _bubbleVisible = false; _moveTimer.Start(); _animTimer.Start(); Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _clickThrough) return;
            _isDragging = true;
            _dragCursorOffset = e.Location;
            _lastDragScreenPos = PointToScreen(e.Location);
            _lastDragTime = Stopwatch.GetTimestamp();
            _velocityX = _velocityY = 0;
            _moveTimer.Stop(); _animTimer.Stop(); _hoverTimer.Stop(); _lingerTimer.Stop(); _flightTimer.Stop();
            _isFlying = false; _isHoveringLive = false; _bubbleVisible = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isDragging) return;
            var screenBounds = Screen.FromPoint(Cursor.Position).WorkingArea;
            var cursor = PointToScreen(e.Location);
            long now = Stopwatch.GetTimestamp();
            float elapsedMs = (now - _lastDragTime) * 1000f / Stopwatch.Frequency;
            if (elapsedMs > 0)
            {

                float sampleX = (cursor.X - _lastDragScreenPos.X) * 3.2f / elapsedMs;
                float sampleY = (cursor.Y - _lastDragScreenPos.Y) * 3.2f / elapsedMs;
                _velocityX = _velocityX * 0.35f + sampleX * 0.65f;
                _velocityY = _velocityY * 0.35f + sampleY * 0.65f;
            }
            _lastDragScreenPos = cursor;
            _lastDragTime = now;
            int newX = Math.Clamp(cursor.X - _dragCursorOffset.X, screenBounds.Left, screenBounds.Right - PetSize);
            int newY = Math.Clamp(cursor.Y - _dragCursorOffset.Y, screenBounds.Top, screenBounds.Bottom - WindowHeight);
            _physicsX = newX; _physicsY = newY;
            _x = newX; _y = newY;
            Location = new Point(newX, newY);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_isDragging) return;
            _isDragging = false;
            _isFlying = true;
            _quietGroundFrames = 0;
            if (Math.Abs(_velocityX) > 0.1f) _direction = _velocityX < 0 ? -1 : 1;
            _flightTimer.Start();
        }

        private void UpdateFlight()
        {
            var area = Screen.FromPoint(Location).WorkingArea;
            _velocityY += Gravity;
            _velocityX *= AirResistance;
            _physicsX += _velocityX;
            _physicsY += _velocityY;
            float maxX = area.Right - PetSize;
            float maxY = area.Bottom - WindowHeight;
            bool onFloor = false;
            if (_physicsX < area.Left) { _physicsX = area.Left; _velocityX = Math.Abs(_velocityX) * WallBounce; }
            else if (_physicsX > maxX) { _physicsX = maxX; _velocityX = -Math.Abs(_velocityX) * WallBounce; }
            if (_physicsY < area.Top) { _physicsY = area.Top; _velocityY = Math.Abs(_velocityY) * WallBounce; }
            else if (_physicsY > maxY)
            {
                _physicsY = maxY;
                _velocityY = -Math.Abs(_velocityY) * FloorBounce;
                onFloor = true;
            }
            if (Math.Abs(_velocityX) > 0.1f) _direction = _velocityX < 0 ? -1 : 1;
            _x = (int)Math.Round(_physicsX); _y = (int)Math.Round(_physicsY);
            Location = new Point(_x, _y);
            if (onFloor && Math.Abs(_velocityY) < StopSpeed && Math.Abs(_velocityX) < StopSpeed)
            {
                if (++_quietGroundFrames >= 3) Land();
            }
            else _quietGroundFrames = 0;
        }

        private void Land()
        {
            _flightTimer.Stop(); _isFlying = false; _velocityX = _velocityY = 0;
            _moveTimer.Start(); _animTimer.Start(); _hoverTimer.Start();
        }

        private void PositionAboveTaskbar()
        {
            var workArea = Screen.PrimaryScreen!.WorkingArea;
            _x = workArea.Left + 40; _y = workArea.Bottom - WindowHeight;
            _physicsX = _x; _physicsY = _y; Location = new Point(_x, _y);
        }

        private void MovePet()
        {
            var workArea = Screen.PrimaryScreen!.WorkingArea;
            _x += _speed * _direction;
            if (_x <= workArea.Left) { _x = workArea.Left; _direction = 1; }
            else if (_x + PetSize >= workArea.Right) { _x = workArea.Right - PetSize; _direction = -1; }
            _y = workArea.Bottom - WindowHeight; _physicsX = _x; _physicsY = _y;
            Location = new Point(_x, _y);
        }

        private void ForceTopmost() => SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        private void ToggleClickThrough()
        {
            _clickThrough = !_clickThrough;
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_LAYERED;
            exStyle = _clickThrough ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);
            if (_clickThrough)
            {
                _isHoveringLive = false; _lingerTimer.Stop(); _bubbleVisible = false;
                if (!_moveTimer.Enabled && !_isFlying) _moveTimer.Start();
                if (!_animTimer.Enabled) _animTimer.Start(); Invalidate();
            }
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            var clickThroughItem = new ToolStripMenuItem("Click-through") { CheckOnClick = true };
            clickThroughItem.Click += (_, _) => ToggleClickThrough(); menu.Items.Add(clickThroughItem);
            var speedUp = new ToolStripMenuItem("Faster"); speedUp.Click += (_, _) => _speed = Math.Min(_speed + 1, 12); menu.Items.Add(speedUp);
            var speedDown = new ToolStripMenuItem("Slower"); speedDown.Click += (_, _) => _speed = Math.Max(_speed - 1, 1); menu.Items.Add(speedDown);
            menu.Items.Add(new ToolStripSeparator());
            var removeItem = new ToolStripMenuItem("Remove"); removeItem.Click += (_, _) => Close(); menu.Items.Add(removeItem);
            ContextMenuStrip = menu;
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE); SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _moveTimer.Stop(); _animTimer.Stop(); _topmostTimer.Stop(); _hoverTimer.Stop(); _lingerTimer.Stop(); _flightTimer.Stop();
            _moveTimer.Dispose(); _animTimer.Dispose(); _topmostTimer.Dispose(); _hoverTimer.Dispose(); _lingerTimer.Dispose(); _flightTimer.Dispose();
            _frame1?.Dispose(); _frame2?.Dispose(); _bubble?.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; bool facingLeft = _direction < 0; int spriteY = WindowHeight - PetSize;
            if (_spritesLoaded && _frame1 != null && _frame2 != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                var frame = _walkFrame ? _frame2 : _frame1;
                if (facingLeft) { var state = g.Save(); g.TranslateTransform(PetSize, 0); g.ScaleTransform(-1, 1); g.DrawImage(frame, 0, spriteY, PetSize, PetSize); g.Restore(state); }
                else g.DrawImage(frame, 0, spriteY, PetSize, PetSize);
            }
            else
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; int legOffset = _walkFrame ? 4 : -4;
                using var bodyBrush = new SolidBrush(Color.FromArgb(255, 120, 170, 240)); using var eyeBrush = new SolidBrush(Color.Black); using var legPen = new Pen(Color.FromArgb(255, 90, 130, 190), 5);
                g.FillEllipse(bodyBrush, 6, spriteY + 10, PetSize - 12, PetSize - 20); g.DrawLine(legPen, 16, spriteY + PetSize - 14, 16 + legOffset, spriteY + PetSize - 2); g.DrawLine(legPen, PetSize - 16, spriteY + PetSize - 14, PetSize - 16 - legOffset, spriteY + PetSize - 2);
                int eyeX = facingLeft ? 12 : PetSize - 18; g.FillEllipse(eyeBrush, eyeX, spriteY + 16, 6, 6);
            }
            if (_bubbleVisible && _bubbleLoaded && _bubble != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; float scale = Math.Min((float)PetSize / _bubble.Width, (float)BubbleAreaHeight / _bubble.Height); int drawW = (int)(_bubble.Width * scale); int drawH = (int)(_bubble.Height * scale); int drawX = (PetSize - drawW) / 2; int drawY = spriteY - drawH; g.DrawImage(_bubble, drawX, drawY, drawW, drawH);
            }
        }
    }
}
