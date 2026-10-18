using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ShadowSnap;

internal sealed class OverlayForm : Form
{
    private readonly Action<Bitmap, string> _onCaptureCompleted;
    private readonly System.Windows.Forms.Timer _detectTimer;
    private readonly Rectangle _virtualBounds;

    private nint _hoveredHwnd = nint.Zero;
    private Rectangle _hoveredRect = Rectangle.Empty;
    private string _hoveredTitle = string.Empty;
    private readonly Bitmap _screenSnapshot;

    private bool _isMouseDown = false;
    private bool _isDragging = false;
    private Point _mouseDownPoint = Point.Empty;
    private Rectangle _dragSelectionRect = Rectangle.Empty;

    public OverlayForm(Action<Bitmap, string> onCaptureCompleted)
    {
        _onCaptureCompleted = onCaptureCompleted;
        _virtualBounds = SystemInformation.VirtualScreen;

        // Capture frozen desktop snapshot before displaying overlay
        _screenSnapshot = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(_screenSnapshot))
        {
            g.CopyFromScreen(_virtualBounds.X, _virtualBounds.Y, 0, 0, _virtualBounds.Size, CopyPixelOperation.SourceCopy);
        }

        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _detectTimer = new System.Windows.Forms.Timer
        {
            Interval = 25
        };
        _detectTimer.Tick += (_, _) => DetectHoveredWindow();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _detectTimer.Start();
        DetectHoveredWindow();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _detectTimer.Stop();
        _detectTimer.Dispose();
        _screenSnapshot?.Dispose();
        base.OnFormClosing(e);
    }

    private void DetectHoveredWindow()
    {
        if (_isDragging || _isMouseDown) return;

        Point cursor = Cursor.Position;
        nint target = FindTopLevelWindowAtPoint(cursor);

        if (target != _hoveredHwnd)
        {
            _hoveredHwnd = target;

            if (_hoveredHwnd != nint.Zero)
            {
                _hoveredRect = WindowCaptureEngine.GetWindowBounds(_hoveredHwnd);
                _hoveredTitle = GetWindowTitle(_hoveredHwnd);
            }
            else
            {
                _hoveredRect = Rectangle.Empty;
                _hoveredTitle = string.Empty;
            }

            Invalidate();
        }
    }

    private nint FindTopLevelWindowAtPoint(Point point)
    {
        nint foundHwnd = nint.Zero;

        // Enumerate windows in z-order
        EnumWindows((hWnd, _) =>
        {
            if (hWnd == Handle) return true; // Skip our overlay
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.IsIconic(hWnd)) return true;

            // Check cloaked
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            StringBuilder className = new(256);
            NativeMethods.GetClassName(hWnd, className, 256);
            string cls = className.ToString();

            // Skip desktop and shell worker windows
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            Rectangle rect = WindowCaptureEngine.GetWindowBounds(hWnd);
            if (rect.Width <= 10 || rect.Height <= 10) return true;

            if (rect.Contains(point))
            {
                foundHwnd = hWnd;
                return false; // Stop enumeration
            }

            return true;
        }, nint.Zero);

        return foundHwnd;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

    private static string GetWindowTitle(nint hWnd)
    {
        StringBuilder sb = new(512);
        NativeMethods.GetWindowText(hWnd, sb, 512);
        string title = sb.ToString().Trim();
        if (string.IsNullOrEmpty(title))
        {
            StringBuilder cls = new(256);
            NativeMethods.GetClassName(hWnd, cls, 256);
            title = cls.ToString();
        }
        return title;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 1. Draw static desktop background
        g.DrawImageUnscaled(_screenSnapshot, 0, 0);

        if (_isDragging && _dragSelectionRect.Width > 0 && _dragSelectionRect.Height > 0)
        {
            // Dim outside dragged selection
            using (SolidBrush ambientBrush = new(Color.FromArgb(85, 0, 0, 0)))
            {
                using Region dimRegion = new(ClientRectangle);
                using GraphicsPath selectionPath = WindowCaptureEngine.CreateRoundedRectanglePath(_dragSelectionRect, 8);
                dimRegion.Exclude(selectionPath);
                g.FillRegion(ambientBrush, dimRegion);
            }

            // Highlight dragged selection
            using (GraphicsPath path = WindowCaptureEngine.CreateRoundedRectanglePath(_dragSelectionRect, 8))
            {
                using (SolidBrush fillBrush = new(Color.FromArgb(30, 0, 122, 255)))
                {
                    g.FillPath(fillBrush, path);
                }

                using (Pen borderPen = new(Color.FromArgb(245, 0, 122, 255), 2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            DrawInfoPill(g, _dragSelectionRect, "Custom Dialog", _dragSelectionRect.Width, _dragSelectionRect.Height);
        }
        else
        {
            // Normal window highlight
            Rectangle localRect = Rectangle.Empty;
            bool hasTarget = _hoveredHwnd != nint.Zero && _hoveredRect.Width > 0 && _hoveredRect.Height > 0;

            if (hasTarget)
            {
                localRect = new Rectangle(
                    _hoveredRect.X - _virtualBounds.X,
                    _hoveredRect.Y - _virtualBounds.Y,
                    _hoveredRect.Width,
                    _hoveredRect.Height);
            }

            // Dim background outside the selected window
            using (SolidBrush ambientBrush = new(Color.FromArgb(85, 0, 0, 0)))
            {
                if (hasTarget)
                {
                    using Region dimRegion = new(ClientRectangle);
                    using GraphicsPath windowPath = WindowCaptureEngine.CreateRoundedRectanglePath(localRect, 8);
                    dimRegion.Exclude(windowPath);
                    g.FillRegion(ambientBrush, dimRegion);
                }
                else
                {
                    g.FillRectangle(ambientBrush, ClientRectangle);
                }
            }

            // Highlight selected window (macOS style blue tint + border)
            if (hasTarget)
            {
                using (GraphicsPath path = WindowCaptureEngine.CreateRoundedRectanglePath(localRect, 8))
                {
                    using (SolidBrush fillBrush = new(Color.FromArgb(40, 0, 122, 255)))
                    {
                        g.FillPath(fillBrush, path);
                    }

                    using (Pen borderPen = new(Color.FromArgb(235, 0, 122, 255), 2.5f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                DrawInfoPill(g, localRect, _hoveredTitle, _hoveredRect.Width, _hoveredRect.Height);
            }
        }

        // Bottom hint bar
        DrawHintBar(g);
    }

    private void DrawInfoPill(Graphics g, Rectangle winRect, string title, int width, int height)
    {
        string label = string.IsNullOrEmpty(title)
            ? $"{width} x {height} px"
            : $"{TruncateString(title, 40)}  •  {width} x {height} px";

        using Font font = new("Segoe UI", 10f, FontStyle.Bold);
        SizeF textSize = g.MeasureString(label, font);

        int pillWidth = (int)textSize.Width + 24;
        int pillHeight = (int)textSize.Height + 12;

        int pillX = winRect.X + ((winRect.Width - pillWidth) / 2);
        int pillY = winRect.Y - pillHeight - 8;

        // Keep inside screen
        if (pillY < 10) pillY = winRect.Y + 12;
        if (pillX < 10) pillX = 10;
        if (pillX + pillWidth > ClientRectangle.Width - 10) pillX = ClientRectangle.Width - pillWidth - 10;

        Rectangle pillRect = new(pillX, pillY, pillWidth, pillHeight);

        using GraphicsPath pillPath = WindowCaptureEngine.CreateRoundedRectanglePath(pillRect, pillHeight / 2);
        using (SolidBrush bgBrush = new(Color.FromArgb(220, 24, 24, 26)))
        {
            g.FillPath(bgBrush, pillPath);
        }
        using (Pen strokePen = new(Color.FromArgb(80, 255, 255, 255), 1.2f))
        {
            g.DrawPath(strokePen, pillPath);
        }

        using SolidBrush textBrush = new(Color.White);
        g.DrawString(label, font, textBrush, pillRect.X + 12, pillRect.Y + 6);
    }

    private void DrawHintBar(Graphics g)
    {
        string hint = "Click: Capture Window   |   Drag: Select Dialog / Area with Shadow   |   Esc: Cancel";
        using Font font = new("Segoe UI", 10.5f, FontStyle.Regular);
        SizeF size = g.MeasureString(hint, font);

        int barWidth = (int)size.Width + 36;
        int barHeight = (int)size.Height + 14;
        int barX = (ClientRectangle.Width - barWidth) / 2;
        int barY = ClientRectangle.Height - barHeight - 25;

        Rectangle barRect = new(barX, barY, barWidth, barHeight);

        using GraphicsPath barPath = WindowCaptureEngine.CreateRoundedRectanglePath(barRect, 10);
        using (SolidBrush bgBrush = new(Color.FromArgb(235, 18, 18, 20)))
        {
            g.FillPath(bgBrush, barPath);
        }
        using (Pen strokePen = new(Color.FromArgb(70, 255, 255, 255), 1f))
        {
            g.DrawPath(strokePen, barPath);
        }

        using SolidBrush textBrush = new(Color.FromArgb(235, 235, 240));
        g.DrawString(hint, font, textBrush, barRect.X + 18, barRect.Y + 7);
    }

    private static string TruncateString(string value, int maxLen)
    {
        return value.Length <= maxLen ? value : string.Concat(value.AsSpan(0, maxLen - 3), "...");
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isMouseDown)
        {
            int dx = Math.Abs(e.X - _mouseDownPoint.X);
            int dy = Math.Abs(e.Y - _mouseDownPoint.Y);

            if (dx > 6 || dy > 6 || _isDragging)
            {
                _isDragging = true;
                int left = Math.Min(_mouseDownPoint.X, e.X);
                int top = Math.Min(_mouseDownPoint.Y, e.Y);
                int width = Math.Abs(e.X - _mouseDownPoint.X);
                int height = Math.Abs(e.Y - _mouseDownPoint.Y);

                _dragSelectionRect = new Rectangle(left, top, width, height);
                Invalidate();
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            _isMouseDown = true;
            _mouseDownPoint = e.Location;
            _isDragging = false;
            _dragSelectionRect = Rectangle.Empty;
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _isMouseDown = false;
                _dragSelectionRect = Rectangle.Empty;
                Invalidate();
            }
            else
            {
                Close();
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left && _isMouseDown)
        {
            _isMouseDown = false;

            if (_isDragging && _dragSelectionRect.Width >= 20 && _dragSelectionRect.Height >= 20)
            {
                ExecuteCustomAreaCapture(_dragSelectionRect);
            }
            else
            {
                _isDragging = false;
                _dragSelectionRect = Rectangle.Empty;
                ExecuteCapture();
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            if (_isDragging && _dragSelectionRect.Width >= 20 && _dragSelectionRect.Height >= 20)
            {
                ExecuteCustomAreaCapture(_dragSelectionRect);
            }
            else
            {
                ExecuteCapture();
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _isMouseDown = false;
                _dragSelectionRect = Rectangle.Empty;
                Invalidate();
            }
            else
            {
                Close();
            }
        }
    }

    private void ExecuteCustomAreaCapture(Rectangle area)
    {
        _detectTimer.Stop();
        Hide();
        Application.DoEvents();

        Rectangle boundsInSnapshot = new(
            Math.Clamp(area.X, 0, _screenSnapshot.Width - 1),
            Math.Clamp(area.Y, 0, _screenSnapshot.Height - 1),
            Math.Min(area.Width, _screenSnapshot.Width - area.X),
            Math.Min(area.Height, _screenSnapshot.Height - area.Y));

        if (boundsInSnapshot.Width <= 10 || boundsInSnapshot.Height <= 10)
        {
            Close();
            return;
        }

        using Bitmap cropped = new(boundsInSnapshot.Width, boundsInSnapshot.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(cropped))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_screenSnapshot, 0, 0, boundsInSnapshot, GraphicsUnit.Pixel);
        }

        // Apply macOS soft multi-layer shadow with 10px rounded corners
        Bitmap finalWithShadow = WindowCaptureEngine.CreateImageWithMacShadow(cropped, shadowOffset: 70, cornerRadius: 10);
        _onCaptureCompleted(finalWithShadow, "Custom Dialog");
        Close();
    }

    private void ExecuteCapture()
    {
        if (_hoveredHwnd == nint.Zero)
        {
            Close();
            return;
        }

        nint target = _hoveredHwnd;
        string title = _hoveredTitle;

        _detectTimer.Stop();
        Hide();
        Application.DoEvents();
        Thread.Sleep(50); // Let overlay disappear from screen buffer

        Bitmap? captured = WindowCaptureEngine.CaptureWindow(target);
        if (captured != null)
        {
            _onCaptureCompleted(captured, title);
        }

        Close();
    }
}
