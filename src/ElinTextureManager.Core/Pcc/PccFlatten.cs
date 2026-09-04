using ElinTextureManager.Core.Identify;

namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// Turns a painted part plus its dye mask into the image the game loads.
///
/// Marked pixels become their own brightness in grey, which is what lets the character's
/// colour come through them cleanly. Unmarked pixels keep exactly the colour they were
/// painted. The shading survives either way - it is the same pixel, only drained of hue.
/// </summary>
public static class PccFlatten
{
    /// <summary>
    /// Perceived brightness. Green counts for more than red and far more than blue
    /// because the eye weighs them that way, so a green and a blue of the same measured
    /// value do not come out as the same grey.
    /// </summary>
    public static byte Luminance(byte r, byte g, byte b) =>
        (byte)Math.Clamp(Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, 255);

    public static PixelBuffer Apply(PccSheet sheet, PccDyeMask mask)
    {
        var buffer = sheet.ToBuffer();

        for (var direction = 0; direction < PccComposer.Rows; direction++)
        for (var frame = 0; frame < PccComposer.Columns; frame++)
        for (var y = 0; y < sheet.CellHeight; y++)
        for (var x = 0; x < sheet.CellWidth; x++)
        {
            if (!mask.Get(direction, frame, x, y)) continue;

            var pixel = sheet.Get(direction, frame, x, y);
            if (pixel.A == 0) continue;

            var grey = Luminance(pixel.R, pixel.G, pixel.B);

            var sx = frame * sheet.CellWidth + x;
            var sy = direction * sheet.CellHeight + y;
            var i = (sy * buffer.Width + sx) * 4;

            buffer.Bgra[i] = grey;
            buffer.Bgra[i + 1] = grey;
            buffer.Bgra[i + 2] = grey;
        }

        return buffer;
    }

    /// <summary>
    /// The picture shown while choosing what dyes: everything drained to grey, with the
    /// marked pixels in red so the choice is visible at a glance rather than guessed at.
    /// </summary>
    public static PixelBuffer Preview(PccSheet sheet, PccDyeMask mask, int direction, int frame)
    {
        var width = sheet.CellWidth;
        var height = sheet.CellHeight;
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = sheet.Get(direction, frame, x, y);
            var i = (y * width + x) * 4;

            if (pixel.A == 0) continue;

            var grey = Luminance(pixel.R, pixel.G, pixel.B);

            if (mask.Get(direction, frame, x, y))
            {
                // Red, but keeping the pixel's own brightness, so the shape and shading
                // still read underneath the marking.
                pixels[i] = (byte)(grey / 4);
                pixels[i + 1] = (byte)(grey / 4);
                pixels[i + 2] = (byte)Math.Clamp(90 + grey * 0.65, 0, 255);
            }
            else
            {
                pixels[i] = grey;
                pixels[i + 1] = grey;
                pixels[i + 2] = grey;
            }

            pixels[i + 3] = pixel.A;
        }

        return new PixelBuffer { Bgra = pixels, Width = width, Height = height };
    }
}
