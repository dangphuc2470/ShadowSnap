using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShadowSnap;

internal static class WindowCaptureEngine
{
    private const int DefaultShadowOffset = 32;

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

    public static Bitmap CreateImageWithMacShadow(Bitmap sourceBitmap, int shadowOffset = DefaultShadowOffset, int cornerRadius = 9)
    {
        int padding = shadowOffset;
        int finalWidth = sourceBitmap.Width + (padding * 2);
        int finalHeight = sourceBitmap.Height + (padding * 2);

        // 1. Create a 1-channel alpha mask for the shadow
        byte[] mask = new byte[finalWidth * finalHeight];

        using (Bitmap maskBmp = new(finalWidth, finalHeight, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(maskBmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Windows 11 light source has a slight top-down angle, offset shadow down by 4px
                Rectangle shadowBaseRect = new(padding, padding + 4, sourceBitmap.Width, sourceBitmap.Height);
                using GraphicsPath path = CreateRoundedRectanglePath(shadowBaseRect, cornerRadius);
                using SolidBrush brush = new(Color.Black);
                g.FillPath(brush, path);
            }

            BitmapData maskData = maskBmp.LockBits(new Rectangle(0, 0, finalWidth, finalHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            unsafe
            {
                byte* p = (byte*)maskData.Scan0;
                int stride = maskData.Stride;
                for (int y = 0; y < finalHeight; y++)
                {
                    byte* row = p + (y * stride);
                    int offset = y * finalWidth;
                    for (int x = 0; x < finalWidth; x++)
                    {
                        mask[offset + x] = row[(x * 4) + 3];
                    }
                }
            }
            maskBmp.UnlockBits(maskData);
        }

        // 2. Perform 3-pass fast horizontal + vertical box blur (Gaussian blur approximation)
        byte[] blurred = FastBoxBlur(mask, finalWidth, finalHeight, r: 16, passes: 3);

        // 3. Render blurred shadow onto output bitmap
        Bitmap output = new(finalWidth, finalHeight, PixelFormat.Format32bppArgb);
        BitmapData outData = output.LockBits(new Rectangle(0, 0, finalWidth, finalHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        // Authentic Windows 11 shadow peak opacity is ~28%
        const double maxOpacity = 0.28;

        unsafe
        {
            byte* pOut = (byte*)outData.Scan0;
            int stride = outData.Stride;
            for (int y = 0; y < finalHeight; y++)
            {
                byte* row = pOut + (y * stride);
                int offset = y * finalWidth;
                for (int x = 0; x < finalWidth; x++)
                {
                    byte val = blurred[offset + x];
                    if (val > 0)
                    {
                        byte a = (byte)(val * maxOpacity);
                        if (a >= 3)
                        {
                            int px = x * 4;
                            row[px + 0] = 0;
                            row[px + 1] = 0;
                            row[px + 2] = 0;
                            row[px + 3] = a;
                        }
                    }
                }
            }
        }
        output.UnlockBits(outData);

        // 4. Draw source window with rounded corners and subtle Windows 11 border
        using (Graphics g = Graphics.FromImage(output))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Inset by 1px to strip off any dirty background border or pixel leakage from behind window
            Rectangle srcCrop = (sourceBitmap.Width > 4 && sourceBitmap.Height > 4)
                ? new(1, 1, sourceBitmap.Width - 2, sourceBitmap.Height - 2)
                : new(0, 0, sourceBitmap.Width, sourceBitmap.Height);

            Rectangle destRect = new(padding, padding, srcCrop.Width, srcCrop.Height);
            using GraphicsPath clipPath = CreateRoundedRectanglePath(destRect, cornerRadius);
            g.SetClip(clipPath);
            g.DrawImage(sourceBitmap, destRect, srcCrop, GraphicsUnit.Pixel);
            g.ResetClip();

            // Subtle Windows 11 window border (1px light neutral stroke)
            using Pen rimPen = new(Color.FromArgb(45, 0, 0, 0), 1f);
            g.DrawPath(rimPen, clipPath);
        }

        return AutoCropTransparent(output);
    }

    private static byte[] FastBoxBlur(byte[] src, int w, int h, int r, int passes)
    {
        byte[] current = (byte[])src.Clone();
        byte[] buffer = new byte[w * h];

        for (int p = 0; p < passes; p++)
        {
            // Horizontal pass
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int sum = 0;
                int count = 0;

                for (int x = -r; x < w; x++)
                {
                    if (x + r < w)
                    {
                        sum += current[row + x + r];
                        count++;
                    }
                    if (x - r - 1 >= 0)
                    {
                        sum -= current[row + x - r - 1];
                        count--;
                    }
                    if (x >= 0)
                    {
                        buffer[row + x] = (byte)(sum / count);
                    }
                }
            }

            // Vertical pass
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                int count = 0;

                for (int y = -r; y < h; y++)
                {
                    if (y + r < h)
                    {
                        sum += buffer[(y + r) * w + x];
                        count++;
                    }
                    if (y - r - 1 >= 0)
                    {
                        sum -= buffer[(y - r - 1) * w + x];
                        count--;
                    }
                    if (y >= 0)
                    {
                        current[y * w + x] = (byte)(sum / count);
                    }
                }
            }
        }

        return current;
    }

    private static Bitmap CaptureWithSyntheticShadow(nint hWnd, Rectangle windowRect, int shadowOffset)
    {
        using Bitmap windowBitmap = new(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(windowBitmap))
        {
            g.CopyFromScreen(windowRect.X, windowRect.Y, 0, 0, windowRect.Size, CopyPixelOperation.SourceCopy);
        }

        return CreateImageWithMacShadow(windowBitmap, shadowOffset, 8);
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
