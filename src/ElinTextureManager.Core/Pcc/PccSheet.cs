using ElinTextureManager.Core.Identify;

namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// An editable PCC sheet: sixteen cells of one part, four frames across four facings.
///
/// Every operation is cell-local, because that is how the thing is actually drawn - a
/// hat is nudged down in the front view without moving the one on the back view, and a
/// pose is copied to the next frame rather than the whole sheet being shifted. Working
/// in sheet coordinates instead would make every edit a sum the caller has to get right.
/// </summary>
public sealed class PccSheet
{
    private readonly byte[] _pixels;

    private PccSheet(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public int CellWidth => Width / PccComposer.Columns;
    public int CellHeight => Height / PccComposer.Rows;

    /// <summary>A blank sheet at the size the game's own parts use.</summary>
    public static PccSheet Blank(int cellWidth = PccComposer.TileWidth,
        int cellHeight = PccComposer.TileHeight)
    {
        var width = cellWidth * PccComposer.Columns;
        var height = cellHeight * PccComposer.Rows;

        return new PccSheet(new byte[width * height * 4], width, height);
    }

    /// <summary>
    /// Takes a copy of a decoded sheet. A copy because editing must never write through
    /// to the file the part was loaded from.
    /// </summary>
    public static PccSheet? From(PixelBuffer buffer)
    {
        if (!PccComposer.LooksLikeSheet(buffer)) return null;

        return new PccSheet((byte[])buffer.Bgra.Clone(), buffer.Width, buffer.Height);
    }

    public PixelBuffer ToBuffer() =>
        new() { Bgra = (byte[])_pixels.Clone(), Width = Width, Height = Height };

    /// <summary>The whole sheet's pixels, for taking an undo snapshot.</summary>
    public byte[] Snapshot() => (byte[])_pixels.Clone();

    public void Restore(byte[] snapshot)
    {
        if (snapshot.Length == _pixels.Length) snapshot.CopyTo(_pixels, 0);
    }

    /// <summary>Where a cell-local point sits in the sheet, or -1 if it is outside the cell.</summary>
    private int IndexOf(int direction, int frame, int x, int y)
    {
        if (x < 0 || y < 0 || x >= CellWidth || y >= CellHeight) return -1;

        var sx = Math.Clamp(frame, 0, PccComposer.Columns - 1) * CellWidth + x;
        var sy = Math.Clamp(direction, 0, PccComposer.Rows - 1) * CellHeight + y;

        return (sy * Width + sx) * 4;
    }

    public (byte B, byte G, byte R, byte A) Get(int direction, int frame, int x, int y)
    {
        var i = IndexOf(direction, frame, x, y);
        if (i < 0) return (0, 0, 0, 0);

        return (_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
    }

    public void Set(int direction, int frame, int x, int y, byte b, byte g, byte r, byte a)
    {
        var i = IndexOf(direction, frame, x, y);
        if (i < 0) return;

        _pixels[i] = b;
        _pixels[i + 1] = g;
        _pixels[i + 2] = r;
        _pixels[i + 3] = a;
    }

    /// <summary>Draws a square nib, which is what a pencil bigger than one pixel is here.</summary>
    public void Draw(int direction, int frame, int x, int y, int size,
        byte b, byte g, byte r, byte a)
    {
        var half = Math.Max(1, size) / 2;

        for (var dy = -half; dy <= half; dy++)
        for (var dx = -half; dx <= half; dx++)
        {
            if (Math.Max(1, size) % 2 == 0 && (dx == half || dy == half)) continue;
            Set(direction, frame, x + dx, y + dy, b, g, r, a);
        }
    }

    /// <summary>
    /// Flood fill inside one cell, four-connected.
    ///
    /// Bounded to the cell on purpose: a fill that leaked across the sheet would repaint
    /// all sixteen poses from one click on one of them.
    /// </summary>
    public void Fill(int direction, int frame, int x, int y, byte b, byte g, byte r, byte a)
    {
        var target = Get(direction, frame, x, y);
        if (target == (b, g, r, a)) return;
        if (IndexOf(direction, frame, x, y) < 0) return;

        var queue = new Queue<(int X, int Y)>();
        var seen = new bool[CellWidth * CellHeight];

        queue.Enqueue((x, y));
        seen[y * CellWidth + x] = true;

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (Get(direction, frame, cx, cy) != target) continue;

            Set(direction, frame, cx, cy, b, g, r, a);

            foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= CellWidth || ny >= CellHeight) continue;

                var key = ny * CellWidth + nx;
                if (seen[key]) continue;

                seen[key] = true;
                queue.Enqueue((nx, ny));
            }
        }
    }

    /// <summary>Flips one cell left to right, for symmetric parts and opposite profiles.</summary>
    public void MirrorCell(int direction, int frame)
    {
        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth / 2; x++)
        {
            var left = Get(direction, frame, x, y);
            var right = Get(direction, frame, CellWidth - 1 - x, y);

            Set(direction, frame, x, y, right.B, right.G, right.R, right.A);
            Set(direction, frame, CellWidth - 1 - x, y, left.B, left.G, left.R, left.A);
        }
    }

    /// <summary>
    /// Shifts one cell by whole pixels. Anything pushed past the edge is gone, which is
    /// the honest behaviour: a hat nudged off the top has left the sprite.
    /// </summary>
    public void NudgeCell(int direction, int frame, int dx, int dy)
    {
        var copy = new (byte B, byte G, byte R, byte A)[CellWidth * CellHeight];

        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
            copy[y * CellWidth + x] = Get(direction, frame, x, y);

        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
        {
            var sx = x - dx;
            var sy = y - dy;

            var pixel = sx < 0 || sy < 0 || sx >= CellWidth || sy >= CellHeight
                ? default
                : copy[sy * CellWidth + sx];

            Set(direction, frame, x, y, pixel.B, pixel.G, pixel.R, pixel.A);
        }
    }

    /// <summary>Copies one cell over another, for reusing a pose across frames.</summary>
    public void CopyCell(int fromDirection, int fromFrame, int toDirection, int toFrame)
    {
        if (fromDirection == toDirection && fromFrame == toFrame) return;

        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
        {
            var pixel = Get(fromDirection, fromFrame, x, y);
            Set(toDirection, toFrame, x, y, pixel.B, pixel.G, pixel.R, pixel.A);
        }
    }

    /// <summary>Empties one cell.</summary>
    public void ClearCell(int direction, int frame)
    {
        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
            Set(direction, frame, x, y, 0, 0, 0, 0);
    }
}
