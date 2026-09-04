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

    /// <summary>
    /// A straight line, by Bresenham.
    ///
    /// Pixel art needs a real line tool: dragging a pencil freehand across 32 pixels
    /// gives a wobble that is visible at this size, and the staircase Bresenham produces
    /// is the one artists expect.
    /// </summary>
    public void DrawLine(int direction, int frame, int x0, int y0, int x1, int y1,
        int size, byte b, byte g, byte r, byte a)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            Draw(direction, frame, x0, y0, size, b, g, r, a);

            if (x0 == x1 && y0 == y1) break;

            var doubled = error * 2;
            if (doubled >= dy) { error += dy; x0 += sx; }
            if (doubled <= dx) { error += dx; y0 += sy; }
        }
    }

    /// <summary>A rectangle from corner to corner, outlined or solid.</summary>
    public void DrawRectangle(int direction, int frame, int x0, int y0, int x1, int y1,
        int size, bool filled, byte b, byte g, byte r, byte a)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        if (filled)
        {
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                Set(direction, frame, x, y, b, g, r, a);

            return;
        }

        DrawLine(direction, frame, left, top, right, top, size, b, g, r, a);
        DrawLine(direction, frame, left, bottom, right, bottom, size, b, g, r, a);
        DrawLine(direction, frame, left, top, left, bottom, size, b, g, r, a);
        DrawLine(direction, frame, right, top, right, bottom, size, b, g, r, a);
    }

    /// <summary>Flips one cell top to bottom.</summary>
    public void FlipCellVertically(int direction, int frame)
    {
        for (var y = 0; y < CellHeight / 2; y++)
        for (var x = 0; x < CellWidth; x++)
        {
            var top = Get(direction, frame, x, y);
            var bottom = Get(direction, frame, x, CellHeight - 1 - y);

            Set(direction, frame, x, y, bottom.B, bottom.G, bottom.R, bottom.A);
            Set(direction, frame, x, CellHeight - 1 - y, top.B, top.G, top.R, top.A);
        }
    }

    /// <summary>
    /// Swaps one colour for another throughout a cell, wherever it appears.
    ///
    /// Not a flood fill: recolouring a garment whose panels are separated by outlines
    /// takes a dozen fills and one replace.
    /// </summary>
    public void ReplaceColour(int direction, int frame,
        (byte B, byte G, byte R, byte A) from, byte b, byte g, byte r, byte a)
    {
        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
            if (Get(direction, frame, x, y) == from) Set(direction, frame, x, y, b, g, r, a);
    }

    /// <summary>
    /// Every colour the sheet actually uses, most-used first.
    ///
    /// A sprite's own palette is the one an artist wants to hand: matching a shade by
    /// eye off a wheel is how a sixteen-colour sprite becomes a forty-colour one.
    /// </summary>
    public IReadOnlyList<(byte B, byte G, byte R)> ColoursUsed(int limit = 24)
    {
        var counts = new Dictionary<int, int>();

        for (var i = 0; i + 3 < _pixels.Length; i += 4)
        {
            if (_pixels[i + 3] < 128) continue;

            var key = _pixels[i] | (_pixels[i + 1] << 8) | (_pixels[i + 2] << 16);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts
            .OrderByDescending(p => p.Value)
            .Take(limit)
            .Select(p => ((byte)(p.Key & 0xFF), (byte)((p.Key >> 8) & 0xFF), (byte)((p.Key >> 16) & 0xFF)))
            .ToList();
    }

    /// <summary>
    /// An ellipse inside the dragged box, outlined or solid.
    ///
    /// Plotted from the ellipse equation rather than by midpoint stepping: at sixteen
    /// pixels across, a circle that is one pixel out of round is a circle that looks
    /// wrong, and the arithmetic is cheap at this size.
    /// </summary>
    public void DrawEllipse(int direction, int frame, int x0, int y0, int x1, int y1,
        int size, bool filled, byte b, byte g, byte r, byte a)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);

        var cx = (left + right) / 2.0;
        var cy = (top + bottom) / 2.0;
        var rx = Math.Max(0.5, (right - left) / 2.0);
        var ry = Math.Max(0.5, (bottom - top) / 2.0);

        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var nx = (x - cx) / rx;
            var ny = (y - cy) / ry;
            var inside = nx * nx + ny * ny;

            if (inside > 1.02) continue;

            // The rim is the band just inside the edge; how wide it is follows the brush.
            if (!filled)
            {
                var innerX = (rx - Math.Max(1, size)) / rx;
                var innerY = (ry - Math.Max(1, size)) / ry;
                var inner = innerX <= 0 || innerY <= 0
                    ? 0
                    : (x - cx) * (x - cx) / (rx - size) / (rx - size)
                      + (y - cy) * (y - cy) / (ry - size) / (ry - size);

                if (inner > 1) Set(direction, frame, x, y, b, g, r, a);
                continue;
            }

            Set(direction, frame, x, y, b, g, r, a);
        }
    }

    /// <summary>
    /// A star, drawn from its centre out to where the drag ended.
    ///
    /// Five points by default, with the inner radius at the proportion that reads as a
    /// star rather than as a cog or a splash.
    /// </summary>
    public void DrawStar(int direction, int frame, int cx, int cy, int toX, int toY,
        int points, int size, bool filled, byte b, byte g, byte r, byte a)
    {
        points = Math.Clamp(points, 3, 12);

        var outer = Math.Max(2, Math.Sqrt((toX - cx) * (toX - cx) + (toY - cy) * (toY - cy)));
        var inner = outer * 0.42;

        // Turned so a point faces up, which is the way a star is always drawn.
        var start = -Math.PI / 2;
        var step = Math.PI / points;

        var corners = new List<(int X, int Y)>();

        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = start + step * i;

            corners.Add(((int)Math.Round(cx + Math.Cos(angle) * radius),
                         (int)Math.Round(cy + Math.Sin(angle) * radius)));
        }

        if (filled) FillPolygon(direction, frame, corners, b, g, r, a);

        for (var i = 0; i < corners.Count; i++)
        {
            var from = corners[i];
            var to = corners[(i + 1) % corners.Count];

            DrawLine(direction, frame, from.X, from.Y, to.X, to.Y, size, b, g, r, a);
        }
    }

    /// <summary>Scanline fill of a closed shape, used by the solid star.</summary>
    private void FillPolygon(int direction, int frame, IReadOnlyList<(int X, int Y)> corners,
        byte b, byte g, byte r, byte a)
    {
        if (corners.Count < 3) return;

        var top = Math.Max(0, corners.Min(c => c.Y));
        var bottom = Math.Min(CellHeight - 1, corners.Max(c => c.Y));

        for (var y = top; y <= bottom; y++)
        {
            var crossings = new List<double>();

            for (var i = 0; i < corners.Count; i++)
            {
                var (x1, y1) = corners[i];
                var (x2, y2) = corners[(i + 1) % corners.Count];

                if (y1 == y2) continue;
                if (y < Math.Min(y1, y2) || y >= Math.Max(y1, y2)) continue;

                crossings.Add(x1 + (y - y1) * (double)(x2 - x1) / (y2 - y1));
            }

            crossings.Sort();

            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var from = (int)Math.Round(crossings[i]);
                var to = (int)Math.Round(crossings[i + 1]);

                for (var x = from; x <= to; x++) Set(direction, frame, x, y, b, g, r, a);
            }
        }
    }
}
