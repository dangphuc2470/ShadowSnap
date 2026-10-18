using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShadowSnap;

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

        return CaptureWithSyntheticShadow(hWnd, windowRect, shadowOffset);
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

        // Add breathing room margin around shadow
        int margin = 8;
        int cropX = Math.Max(0, left - margin);
        int cropY = Math.Max(0, top - margin);
        int cropR = Math.Min(width - 1, right + margin);
        int cropB = Math.Min(height - 1, bottom + margin);
        int cropWidth = cropR - cropX + 1;
        int cropHeight = cropB - cropY + 1;

        if (cropWidth <= 0 || cropHeight <= 0) return src;
        if (cropWidth == width && cropHeight == height) return src;

        Rectangle cropRect = new(cropX, cropY, cropWidth, cropHeight);
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
