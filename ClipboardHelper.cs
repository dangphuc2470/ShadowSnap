using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShadowSnap;

internal static class ClipboardHelper
{
    public static void SetClipboardImage(Bitmap bitmap)
    {
        if (bitmap == null) return;

        // Use standard Retry mechanism because clipboard may be locked momentarily by other apps
        for (int retry = 0; retry < 5; retry++)
        {
            try
            {
                IDataObject dataObject = new DataObject();

                // 1. Memory stream of PNG format (widely supported by modern apps for alpha transparency)
                MemoryStream pngStream = new();
                bitmap.Save(pngStream, ImageFormat.Png);
                pngStream.Position = 0;
                dataObject.SetData("PNG", false, pngStream);

                // 2. Standard bitmap fallback
                dataObject.SetData(DataFormats.Bitmap, true, bitmap);

                // 3. DIBV5 format (supports 32-bit BGRA with alpha for Windows native paste)
                byte[]? dib = CreateDibV5(bitmap);
                if (dib != null)
                {
                    MemoryStream dibStream = new(dib);
                    dataObject.SetData(DataFormats.Dib, false, dibStream);
                }

                Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static byte[]? CreateDibV5(Bitmap bitmap)
    {
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int headerSize = 124; // BITMAPV5HEADER size
                int imageSize = width * height * 4;
                byte[] buffer = new byte[headerSize + imageSize];

                using (BinaryWriter writer = new(new MemoryStream(buffer)))
                {
                    // bV5Size
                    writer.Write(headerSize);
                    // bV5Width
                    writer.Write(width);
                    // bV5Height (positive = bottom-up DIB)
                    writer.Write(height);
                    // bV5Planes
                    writer.Write((short)1);
                    // bV5BitCount
                    writer.Write((short)32);
                    // bV5Compression (BI_BITFIELDS = 3)
                    writer.Write(3);
                    // bV5SizeImage
                    writer.Write(imageSize);
                    // bV5XPelsPerMeter
                    writer.Write(0);
                    // bV5YPelsPerMeter
                    writer.Write(0);
                    // bV5ClrUsed
                    writer.Write(0);
                    // bV5ClrImportant
                    writer.Write(0);
                    // bV5RedMask
                    writer.Write(0x00FF0000);
                    // bV5GreenMask
                    writer.Write(0x0000FF00);
                    // bV5BlueMask
                    writer.Write(0x000000FF);
                    // bV5AlphaMask
                    writer.Write(unchecked((int)0xFF000000));
                    // bV5CSType (sRGB = 'sRGB')
                    writer.Write(0x73524742);
                    // bV5Endpoints (36 bytes zeroed)
                    writer.Write(new byte[36]);
                    // bV5GammaRed, Green, Blue
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    // bV5Intent (LCS_GM_IMAGES = 4)
                    writer.Write(4);
                    // bV5ProfileData, bV5ProfileSize, bV5Reserved
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    // Write bottom-up pixel rows
                    byte[] rowBuffer = new byte[width * 4];
                    for (int y = height - 1; y >= 0; y--)
                    {
                        nint srcRow = data.Scan0 + (y * data.Stride);
                        Marshal.Copy(srcRow, rowBuffer, 0, width * 4);
                        writer.Write(rowBuffer);
                    }
                }

                return buffer;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch
        {
            return null;
        }
    }
}
