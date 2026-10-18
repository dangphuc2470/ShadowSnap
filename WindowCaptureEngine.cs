using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacWindowSnap;

internal static class WindowCaptureEngine
{
    private const int DefaultShadowOffset = 70;

    public static Rectangle GetWindowBounds(nint hWnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(
            hWnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT rect,
            Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            return rect.ToRectangle();
        }

        NativeMethods.GetWindowRect(hWnd, out rect);
        return rect.ToRectangle();
    }

    public static Bitmap? CaptureWindow(nint hWnd, int shadowOffset = DefaultShadowOffset)
    {
        if (hWnd == nint.Zero) return null;

        // Restore minimized window if necessary
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            Thread.Sleep(200);
        }

        // Bring window to top for clean capture
        NativeMethods.SetForegroundWindow(hWnd);
        NativeMethods.BringWindowToTop(hWnd);
        Thread.Sleep(60);

        Rectangle windowRect = GetWindowBounds(hWnd);
        if (windowRect.Width <= 0 || windowRect.Height <= 0) return null;

        // Try authentic DWM drop-shadow extraction first
        Bitmap? authenticImage = CaptureWithAuthenticShadow(hWnd, windowRect, shadowOffset);
        if (authenticImage != null)
        {
            return authenticImage;
        }

        // Fallback: Capture standard window and apply synthetic soft shadow
        return CaptureWithSyntheticShadow(hWnd, windowRect, shadowOffset);
    }

    private static Bitmap? CaptureWithAuthenticShadow(nint targetHwnd, Rectangle windowRect, int shadowOffset)
    {
        Rectangle bounds = windowRect;
        bounds.Inflate(shadowOffset, shadowOffset);

        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        bounds.Intersect(virtualScreen);

        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        Bitmap? white1 = null;
        Bitmap? black = null;
        Bitmap? white2 = null;

        try
        {
            using (Form bgForm = CreateLayeredBackgroundForm(bounds))
            {
                // Position background right behind target window
                NativeMethods.SetWindowPos(
                    bgForm.Handle,
                    targetHwnd,
                    bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

                // Pass 1: White background
                bgForm.BackColor = Color.White;
                FlushDwm();
                white1 = CaptureScreenRegion(bounds);

                // Pass 2: Black background
                bgForm.BackColor = Color.Black;
                FlushDwm();
                black = CaptureScreenRegion(bounds);

                // Pass 3: White background (validation pass)
                bgForm.BackColor = Color.White;
                FlushDwm();
                white2 = CaptureScreenRegion(bounds);
            }

            if (white1 == null || black == null || white2 == null) return null;

            Bitmap transparent;
            if (AreImagesSimilar(white1, white2))
            {
                transparent = CalculateAlphaChannel(white1, black);
            }
            else
            {
                transparent = (Bitmap)white2.Clone();
            }

            return AutoCropTransparent(transparent);
        }
        catch
        {
            return null;
        }
        finally
        {
            white1?.Dispose();
            black?.Dispose();
            white2?.Dispose();
        }
    }

    private static Form CreateLayeredBackgroundForm(Rectangle bounds)
    {
        Form form = new()
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = bounds,
            ShowInTaskbar = false,
            TopMost = false,
            Enabled = false
        };

        // Ensure WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        form.HandleCreated += (_, _) =>
        {
            int exStyle = NativeMethods.GetWindowLong(form.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(form.Handle, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };

        form.Show();
        return form;
    }

    private static void FlushDwm()
    {
        NativeMethods.DwmFlush();
        Thread.Sleep(20);
    }

    private static Bitmap CaptureScreenRegion(Rectangle rect)
    {
        Bitmap bitmap = new(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    private static unsafe Bitmap CalculateAlphaChannel(Bitmap white, Bitmap black)
    {
        int width = white.Width;
        int height = white.Height;

        Bitmap result = new(width, height, PixelFormat.Format32bppArgb);

        BitmapData whiteData = white.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData blackData = black.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            byte* pWhite = (byte*)whiteData.Scan0;
            byte* pBlack = (byte*)blackData.Scan0;
            byte* pResult = (byte*)resultData.Scan0;

            int stride = whiteData.Stride;

            for (int y = 0; y < height; y++)
            {
                byte* rowW = pWhite + (y * stride);
                byte* rowB = pBlack + (y * stride);
                byte* rowR = pResult + (y * stride);

                for (int x = 0; x < width; x++)
                {
                    int i = x * 4;
                    byte bW = rowW[i];
                    byte gW = rowW[i + 1];
                    byte rW = rowW[i + 2];

                    byte bB = rowB[i];
                    byte gB = rowB[i + 1];
                    byte rB = rowB[i + 2];

                    // Alpha extraction formula
                    double alpha = (rB - rW + 255) / 255.0;

                    if (alpha >= 0.999)
                    {
                        rowR[i] = bW;
                        rowR[i + 1] = gW;
                        rowR[i + 2] = rW;
                        rowR[i + 3] = 255;
                    }
                    else if (alpha > 0.005)
                    {
                        rowR[i] = (byte)Math.Clamp(bB / alpha, 0, 255);
                        rowR[i + 1] = (byte)Math.Clamp(gB / alpha, 0, 255);
                        rowR[i + 2] = (byte)Math.Clamp(rB / alpha, 0, 255);
                        rowR[i + 3] = (byte)Math.Clamp(255 * alpha, 0, 255);
                    }
                    else
                    {
                        rowR[i] = 0;
                        rowR[i + 1] = 0;
                        rowR[i + 2] = 0;
                        rowR[i + 3] = 0;
                    }
                }
            }
        }
        finally
        {
            white.UnlockBits(whiteData);
            black.UnlockBits(blackData);
            result.UnlockBits(resultData);
        }

        return result;
    }

    private static unsafe bool AreImagesSimilar(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;

        BitmapData dataA = a.LockBits(new Rectangle(0, 0, a.Width, a.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData dataB = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int sampleStep = 8;
        int diffCount = 0;
        int checkedCount = 0;

        try
        {
            byte* pA = (byte*)dataA.Scan0;
            byte* pB = (byte*)dataB.Scan0;
            int stride = dataA.Stride;

            for (int y = 0; y < a.Height; y += sampleStep)
            {
                byte* rowA = pA + (y * stride);
                byte* rowB = pB + (y * stride);

                for (int x = 0; x < a.Width; x += sampleStep)
                {
                    int i = x * 4;
                    checkedCount++;
                    if (Math.Abs(rowA[i] - rowB[i]) > 8 ||
                        Math.Abs(rowA[i + 1] - rowB[i + 1]) > 8 ||
                        Math.Abs(rowA[i + 2] - rowB[i + 2]) > 8)
                    {
                        diffCount++;
                    }
                }
            }

            return diffCount <= (checkedCount * 0.05); // Under 5% difference
        }
        finally
        {
            a.UnlockBits(dataA);
            b.UnlockBits(dataB);
        }
    }

    private static unsafe Bitmap AutoCropTransparent(Bitmap src)
    {
        int width = src.Width;
        int height = src.Height;

        BitmapData data = src.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int top = 0, bottom = height - 1, left = 0, right = width - 1;

        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;

            // Find Top
            bool found = false;
            for (int y = 0; y < height; y++)
            {
                byte* row = scan0 + (y * stride);
                for (int x = 0; x < width; x++)
                {
                    if (row[(x * 4) + 3] > 0)
                    {
                        top = y;
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            if (!found) return src; // Entirely transparent

            // Find Bottom
            found = false;
            for (int y = height - 1; y >= top; y--)
            {
                byte* row = scan0 + (y * stride);
                for (int x = 0; x < width; x++)
                {
                    if (row[(x * 4) + 3] > 0)
                    {
                        bottom = y;
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            // Find Left
            found = false;
            for (int x = 0; x < width; x++)
            {
                for (int y = top; y <= bottom; y++)
                {
                    byte* row = scan0 + (y * stride);
                    if (row[(x * 4) + 3] > 0)
                    {
                        left = x;
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            // Find Right
            found = false;
            for (int x = width - 1; x >= left; x--)
            {
                for (int y = top; y <= bottom; y++)
                {
                    byte* row = scan0 + (y * stride);
                    if (row[(x * 4) + 3] > 0)
                    {
                        right = x;
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }
        }
        finally
        {
            src.UnlockBits(data);
        }

        // Add 6px breathing room padding if bounds allow
        int cropWidth = right - left + 1;
        int cropHeight = bottom - top + 1;

        if (cropWidth <= 0 || cropHeight <= 0) return src;
        if (cropWidth == width && cropHeight == height) return src;

        Rectangle cropRect = new(left, top, cropWidth, cropHeight);
        Bitmap cropped = new(cropWidth, cropHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(cropped))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(src, 0, 0, cropRect, GraphicsUnit.Pixel);
        }

        src.Dispose();
        return cropped;
    }

    public static Bitmap CreateImageWithMacShadow(Bitmap sourceBitmap, int shadowOffset = DefaultShadowOffset, int cornerRadius = 10)
    {
        int padding = shadowOffset;
        int finalWidth = sourceBitmap.Width + (padding * 2);
        int finalHeight = sourceBitmap.Height + (padding * 2);

        Bitmap output = new(finalWidth, finalHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(output))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle destRect = new(padding, padding, sourceBitmap.Width, sourceBitmap.Height);

            // Layer 1: Ambient soft blur shadow (wide spread, low opacity)
            int ambientLayers = 24;
            for (int i = ambientLayers; i >= 1; i--)
            {
                double factor = (double)i / ambientLayers;
                int spread = (int)(padding * 0.85 * factor);
                int alpha = (int)(20 * (1.0 - factor));
                if (alpha <= 0) continue;

                Rectangle layerRect = new(
                    destRect.X - spread,
                    destRect.Y - spread + (int)(6 * factor),
                    destRect.Width + (spread * 2),
                    destRect.Height + (spread * 2));

                using GraphicsPath path = CreateRoundedRectanglePath(layerRect, cornerRadius + (int)(spread * 0.5));
                using SolidBrush brush = new(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(brush, path);
            }

            // Layer 2: Key directional shadow (closer and darker, slightly offset downwards)
            int keyLayers = 16;
            for (int i = keyLayers; i >= 1; i--)
            {
                double factor = (double)i / keyLayers;
                int spread = (int)(padding * 0.35 * factor);
                int alpha = (int)(36 * (1.0 - factor));
                if (alpha <= 0) continue;

                Rectangle layerRect = new(
                    destRect.X - spread,
                    destRect.Y - spread + (int)(10 * factor),
                    destRect.Width + (spread * 2),
                    destRect.Height + (spread * 2));

                using GraphicsPath path = CreateRoundedRectanglePath(layerRect, cornerRadius + (int)(spread * 0.3));
                using SolidBrush brush = new(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(brush, path);
            }

            // Clip and draw source image with rounded corners
            using (GraphicsPath clipPath = CreateRoundedRectanglePath(destRect, cornerRadius))
            {
                g.SetClip(clipPath);
                g.DrawImage(sourceBitmap, destRect);
                g.ResetClip();

                // Subtle macOS-style rim stroke around the window
                using Pen rimPen = new(Color.FromArgb(35, 0, 0, 0), 1f);
                g.DrawPath(rimPen, clipPath);
            }
        }

        return AutoCropTransparent(output);
    }

    private static Bitmap CaptureWithSyntheticShadow(nint hWnd, Rectangle windowRect, int shadowOffset)
    {
        using Bitmap windowBitmap = new(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(windowBitmap))
        {
            g.CopyFromScreen(windowRect.X, windowRect.Y, 0, 0, windowRect.Size, CopyPixelOperation.SourceCopy);
        }

        return CreateImageWithMacShadow(windowBitmap, shadowOffset, 10);
    }

    public static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int cornerRadius)
    {
        GraphicsPath path = new();
        int diameter = cornerRadius * 2;

        if (rect.Width < diameter || rect.Height < diameter)
        {
            path.AddRectangle(rect);
            return path;
        }

        Rectangle arc = new(rect.X, rect.Y, diameter, diameter);

        // Top left
        path.AddArc(arc, 180, 90);

        // Top right
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom right
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom left
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
