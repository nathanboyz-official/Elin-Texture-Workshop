using ElinTextureManager.Core.Identify;

namespace ElinTextureManager.Core.Pcc;

/// <summary>One chosen part, ready to draw.</summary>
public sealed class PccPiece
{
    public required string Layer { get; init; }
    public required PixelBuffer Sheet { get; init; }

    /// <summary>Where it came from, so the preview can say which mod supplied it.</summary>
    public string? SourcePath { get; init; }
    public string? ModName { get; init; }

    /// <summary>
    /// The colour this part is dyed, or null to draw it as authored.
    ///
    /// PCC parts are drawn greyscale precisely so they can be tinted per character - it
    /// is why the same hairstyle appears on a hundred different heads in a hundred
    /// different colours, and why a style file stores six hex digits beside every part.
    /// </summary>
    public (byte R, byte G, byte B)? Tint { get; init; }

    public int Order => PccLayer.OrderOf(Layer);
}

/// <summary>
/// Builds one frame of a character out of the PCC parts installed across every mod.
///
/// A PCC sheet is a grid: four columns of animation frames across four rows of facing
/// directions. Every part uses the same grid, so a hair from one mod and a coat from
/// another line up cell for cell, which is the whole reason a character can be assembled
/// from pieces at all.
///
/// Elin does this while it draws, and only for characters that exist in a save. Doing it
/// here is what turns three thousand greyscale sheets - which tell you nothing on their
/// own, and which the picture search cannot help with because the game tints them at
/// runtime - into something you can actually look at before installing anything.
/// </summary>
public static class PccComposer
{
    /// <summary>Frames across, and directions down. Fixed by the format.</summary>
    public const int Columns = 4;
    public const int Rows = 4;

    /// <summary>The canvas each part is drawn onto, before any scaling.</summary>
    public const int TileWidth = 32;
    public const int TileHeight = 48;

    /// <summary>
    /// Draws one cell of every piece on top of each other, back to front.
    ///
    /// <paramref name="scale"/> is a whole-number magnification: these are 32x48 pixel
    /// sprites, and anything smooth would turn them to mush.
    /// </summary>
    public static PixelBuffer Compose(IEnumerable<PccPiece> pieces, int direction, int frame,
        int scale = 4)
    {
        scale = Math.Max(1, scale);

        var width = TileWidth * scale;
        var height = TileHeight * scale;
        var canvas = new byte[width * height * 4];

        foreach (var piece in pieces.OrderBy(p => p.Order))
        {
            if (piece.Order < 0) continue;
            DrawCell(canvas, width, height, piece.Sheet, direction, frame, piece.Tint);
        }

        return new PixelBuffer { Bgra = canvas, Width = width, Height = height };
    }

    /// <summary>
    /// Composites one cell of a sheet onto the canvas.
    ///
    /// The cell size is read from the sheet rather than assumed: with Variable Sprite
    /// Support installed a part can be any resolution, so long as the sheet is still
    /// four cells by four. Sampling by proportion rather than by pixel is what lets a
    /// 64x96 coat sit correctly on a 32x48 body.
    /// </summary>
    private static void DrawCell(byte[] canvas, int width, int height,
        PixelBuffer sheet, int direction, int frame, (byte R, byte G, byte B)? tint)
    {
        var cellWidth = sheet.Width / Columns;
        var cellHeight = sheet.Height / Rows;
        if (cellWidth <= 0 || cellHeight <= 0) return;

        var originX = Math.Clamp(frame, 0, Columns - 1) * cellWidth;
        var originY = Math.Clamp(direction, 0, Rows - 1) * cellHeight;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = originX + x * cellWidth / width;
            var sy = originY + y * cellHeight / height;

            if (sx >= sheet.Width || sy >= sheet.Height) continue;

            var source = (sy * sheet.Width + sx) * 4;
            var alpha = sheet.Bgra[source + 3];
            if (alpha == 0) continue;

            var target = (y * width + x) * 4;
            Blend(canvas, target, sheet.Bgra, source, alpha, tint);
        }
    }

    /// <summary>
    /// Neutral grey. A pixel this bright comes out exactly the chosen colour; darker
    /// pixels are shadow, lighter ones are highlight.
    ///
    /// Measured from the parts themselves rather than assumed. Across vanilla hair,
    /// cloth, body and eye sheets the opaque pixels average 113 to 164 and never reach
    /// white - they are drawn around the middle of the range on purpose, so that dyeing
    /// can go both up and down from there. Treating white as neutral instead would make
    /// every character come out roughly half as bright as the colour they picked.
    /// </summary>
    private const int NeutralGrey = 128;

    /// <summary>
    /// Dyes one channel of a greyscale part.
    ///
    /// Multiply rather than replace, because the grey is not a placeholder - it is the
    /// shading, and the folds and highlights the artist drew have to survive the dye.
    /// </summary>
    private static byte Dye(byte channel, byte tint) =>
        (byte)Math.Min(255, channel * tint / NeutralGrey);

    /// <summary>Ordinary source-over compositing, on straight (not premultiplied) alpha.</summary>
    private static void Blend(byte[] canvas, int target, byte[] source, int from, byte alpha,
        (byte R, byte G, byte B)? tint)
    {
        var b = source[from];
        var g = source[from + 1];
        var r = source[from + 2];

        if (tint is { } colour)
        {
            b = Dye(b, colour.B);
            g = Dye(g, colour.G);
            r = Dye(r, colour.R);
        }

        if (alpha == 255)
        {
            canvas[target] = b;
            canvas[target + 1] = g;
            canvas[target + 2] = r;
            canvas[target + 3] = 255;
            return;
        }

        var a = alpha / 255.0;
        var existing = canvas[target + 3] / 255.0;
        var outAlpha = a + existing * (1 - a);

        if (outAlpha <= 0)
        {
            canvas[target + 3] = 0;
            return;
        }

        var dyed = new[] { b, g, r };

        for (var c = 0; c < 3; c++)
        {
            var src = dyed[c] / 255.0;
            var dst = canvas[target + c] / 255.0;
            canvas[target + c] = (byte)Math.Round(
                (src * a + dst * existing * (1 - a)) / outAlpha * 255);
        }

        canvas[target + 3] = (byte)Math.Round(outAlpha * 255);
    }

    /// <summary>
    /// Whether a sheet is laid out the way the format requires. Used to say why a part
    /// will not draw, rather than drawing it wrong.
    /// </summary>
    public static bool LooksLikeSheet(PixelBuffer sheet) =>
        sheet is { Width: > 0, Height: > 0 }
        && sheet.Width % Columns == 0
        && sheet.Height % Rows == 0;
}
