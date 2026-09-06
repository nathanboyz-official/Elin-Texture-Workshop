using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Identify;

namespace ElinTextureManager.App.Imaging;

/// <summary>
/// Turns image files into the plain BGRA buffers the matcher works on.
///
/// Decoding lives here rather than in Core because Core has no framework imaging and is
/// better for it: the matching itself is arithmetic over byte arrays, which is testable
/// without a single decoded PNG.
/// </summary>
public static class PixelDecoder
{
    /// <summary>
    /// Decodes to straight (non-premultiplied) BGRA, capped to <paramref name="maxSide"/>.
    ///
    /// The cap is what makes the pixel pass affordable: a 2048px sheet compared at full
    /// size costs sixty times what it costs at 256, and tells you nothing more about
    /// whether it is the same character.
    /// </summary>
    public static PixelBuffer? Decode(string path, int maxSide = 0)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return Convert(frame, maxSide);
        }
        catch
        {
            return null;
        }
    }

    public static PixelBuffer? Convert(BitmapSource source, int maxSide = 0)
    {
        try
        {
            BitmapSource src = source;

            var longest = Math.Max(src.PixelWidth, src.PixelHeight);
            if (maxSide > 0 && longest > maxSide)
            {
                var factor = maxSide / (double)longest;
                src = new TransformedBitmap(src, new ScaleTransform(factor, factor));
            }

            // Bgra32 rather than Pbgra32: premultiplied colour is darkened by its own
            // alpha, which would make every semi-transparent edge pixel look wrong.
            var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            var w = converted.PixelWidth;
            var h = converted.PixelHeight;
            if (w < 2 || h < 2) return null;

            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);

            return new PixelBuffer { Bgra = pixels, Width = w, Height = h };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Trims fully transparent margins. A sprite sheet is mostly empty space, and the
    /// palette of empty space is nothing.
    /// </summary>
    public static PixelBuffer Trim(PixelBuffer buffer, byte alphaFloor = 16)
    {
        int minX = buffer.Width, minY = buffer.Height, maxX = -1, maxY = -1;

        for (var y = 0; y < buffer.Height; y++)
        for (var x = 0; x < buffer.Width; x++)
        {
            if (buffer.Bgra[(y * buffer.Width + x) * 4 + 3] < alphaFloor) continue;

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        if (maxX < 0) return buffer;

        var w = maxX - minX + 1;
        var h = maxY - minY + 1;
        if (w == buffer.Width && h == buffer.Height) return buffer;

        var trimmed = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            Array.Copy(buffer.Bgra, ((minY + y) * buffer.Width + minX) * 4,
                trimmed, y * w * 4, w * 4);
        }

        return new PixelBuffer { Bgra = trimmed, Width = w, Height = h };
    }

    /// <summary>
    /// Turns a buffer back into something WPF can show.
    ///
    /// Frozen before it is returned: these are built on a background thread and handed
    /// to the UI, and an unfrozen BitmapSource cannot cross threads.
    /// </summary>
    public static BitmapSource? ToBitmap(PixelBuffer? buffer)
    {
        if (buffer is null || buffer.IsEmpty) return null;

        try
        {
            var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96,
                PixelFormats.Bgra32, null, buffer.Bgra, buffer.Width * 4);

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
