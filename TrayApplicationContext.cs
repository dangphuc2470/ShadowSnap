using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ShadowSnap;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int HotkeyId = 9999;
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private bool _autoSave = true;
    private bool _playSound = true;
    private bool _isCapturing = false;

    public TrayApplicationContext()
    {
        _hotkeyWindow = new HotkeyWindow(OnHotkeyPressed);

        _trayIcon = new NotifyIcon
        {
            Icon = CreateAppIcon(),
            Text = "ShadowSnap (Ctrl + Shift + A)",
            Visible = true
        };

        BuildContextMenu();

        RegisterGlobalHotkey();
    }

    private void BuildContextMenu()
    {
        ContextMenuStrip menu = new();

        ToolStripMenuItem captureItem = new("Capture Window (Ctrl + Shift + A)", null, (_, _) => StartCapture())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(captureItem);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem autoSaveItem = new("Auto-save to Pictures/Screenshots", null, (s, _) =>
        {
            if (s is ToolStripMenuItem item)
            {
                _autoSave = !_autoSave;
                item.Checked = _autoSave;
            }
        })
        {
            Checked = _autoSave
        };
        menu.Items.Add(autoSaveItem);

        ToolStripMenuItem soundItem = new("Play Camera Sound", null, (s, _) =>
        {
            if (s is ToolStripMenuItem item)
            {
                _playSound = !_playSound;
                item.Checked = _playSound;
            }
        })
        {
            Checked = _playSound
        };
        menu.Items.Add(soundItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Open Screenshots Folder", null, (_, _) =>
        {
            string folder = GetScreenshotsDirectory();
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
        }));

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => StartCapture();
    }

    private void RegisterGlobalHotkey()
    {
        bool success = NativeMethods.RegisterHotKey(
            _hotkeyWindow.Handle,
            HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            (uint)Keys.A);

        if (!success)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                "ShadowSnap",
                "Hotkey Ctrl + Shift + A may be in use by another application. You can still trigger capture via the system tray icon.",
                ToolTipIcon.Warning);
        }
    }

    private void OnHotkeyPressed()
    {
        StartCapture();
    }

    public void StartCapture()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            OverlayForm overlay = new(OnCaptureCompleted);
            overlay.FormClosed += (_, _) => _isCapturing = false;
            overlay.Show();
            overlay.Activate();
        }
        catch
        {
            _isCapturing = false;
        }
    }

    private void OnCaptureCompleted(Bitmap capturedImage, string windowTitle)
    {
        try
        {
            // 1. Copy to clipboard
            ClipboardHelper.SetClipboardImage(capturedImage);

            // 2. Play sound
            if (_playSound)
            {
                SoundHelper.PlayCameraShutter();
            }

            // 3. Save to Screenshots folder
            string savedPath = string.Empty;
            if (_autoSave)
            {
                savedPath = SaveToScreenshots(capturedImage, windowTitle);
            }
            else
            {
                // Temporary file for hero toast preview
                string tempDir = Path.Combine(Path.GetTempPath(), "ShadowSnap");
                Directory.CreateDirectory(tempDir);
                savedPath = Path.Combine(tempDir, "temp_screenshot.png");
                capturedImage.Save(savedPath, ImageFormat.Png);
            }

            // 4. Show Windows native Snipping Tool style toast notification
            ToastHelper.ShowScreenshotToast(savedPath);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "Capture Error", ex.Message, ToolTipIcon.Error);
        }
    }

    private static string GetScreenshotsDirectory()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string screenshots = Path.Combine(pictures, "Screenshots");
        if (!Directory.Exists(screenshots))
        {
            Directory.CreateDirectory(screenshots);
        }
        return screenshots;
    }

    private static string SaveToScreenshots(Bitmap bitmap, string title)
    {
        string dir = GetScreenshotsDirectory();
        string cleanTitle = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(cleanTitle)) cleanTitle = "Window";
        if (cleanTitle.Length > 30) cleanTitle = cleanTitle[..30];

        string fileName = $"Snap_{DateTime.Now:yyyyMMdd_HHmmss}_{cleanTitle}.png";
        string filePath = Path.Combine(dir, fileName);

        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }

    private static Icon CreateAppIcon()
    {
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app.ico");
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath, 32, 32);
            }

            Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null)
            {
                return new Icon(appIcon, 32, 32);
            }
        }
        catch
        {
            // Fallback to generated icon
        }

        using Bitmap bmp = new(32, 32, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Gradient rounded box (App icon style)
            Rectangle rect = new(2, 2, 28, 28);
            using GraphicsPath path = WindowCaptureEngine.CreateRoundedRectanglePath(rect, 6);
            using LinearGradientBrush brush = new(rect, Color.FromArgb(0, 122, 255), Color.FromArgb(88, 86, 214), 45f);
            g.FillPath(brush, path);

            // Inner camera / window icon
            using Pen pen = new(Color.White, 2f);
            g.DrawRectangle(pen, 7, 7, 18, 14);
            g.DrawLine(pen, 7, 11, 25, 11);

            // Circle lens
            g.DrawEllipse(pen, 13, 14, 6, 5);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApplication()
    {
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _hotkeyWindow.DestroyHandle();
        ExitThread();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                _onHotkey();
            }

            base.WndProc(ref m);
        }
    }
}
