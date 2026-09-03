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
            DrawCell(canvas, width, height, piece.Sheet, direction, frame);
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
        PixelBuffer sheet, int direction, int frame)
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
            Blend(canvas, target, sheet.Bgra, source, alpha);
        }
    }

    /// <summary>Ordinary source-over compositing, on straight (not premultiplied) alpha.</summary>
    private static void Blend(byte[] canvas, int target, byte[] source, int from, byte alpha)
    {
        if (alpha == 255)
        {
            canvas[target] = source[from];
            canvas[target + 1] = source[from + 1];
            canvas[target + 2] = source[from + 2];
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

        for (var c = 0; c < 3; c++)
        {
            var src = source[from + c] / 255.0;
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
