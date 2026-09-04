using ElinTextureManager.Core.Identify;

namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// Which pixels of a part should take the character's dye.
///
/// Elin multiplies a whole part by one colour as it draws, so a pixel drawn in grey
/// comes out as whatever colour the character wears, while a pixel drawn in colour keeps
/// its own hue through the multiply. That is the whole trick behind PCC art, and it is
/// the reason for painting in colour first and deciding afterwards: draw the thing as it
/// should look, then say which of it is cloth that should follow the character and which
/// is a brass buckle that should stay brass.
/// </summary>
public sealed class PccDyeMask
{
    private readonly bool[] _dyeable;

    public PccDyeMask(int width, int height)
    {
        Width = width;
        Height = height;
        _dyeable = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public int CellWidth => Width / PccComposer.Columns;
    public int CellHeight => Height / PccComposer.Rows;

    private int IndexOf(int direction, int frame, int x, int y)
    {
        if (x < 0 || y < 0 || x >= CellWidth || y >= CellHeight) return -1;

        var sx = Math.Clamp(frame, 0, PccComposer.Columns - 1) * CellWidth + x;
        var sy = Math.Clamp(direction, 0, PccComposer.Rows - 1) * CellHeight + y;

        return sy * Width + sx;
    }

    public bool Get(int direction, int frame, int x, int y)
    {
        var i = IndexOf(direction, frame, x, y);
        return i >= 0 && _dyeable[i];
    }

    public void Set(int direction, int frame, int x, int y, bool dyeable)
    {
        var i = IndexOf(direction, frame, x, y);
        if (i >= 0) _dyeable[i] = dyeable;
    }

    /// <summary>Paints a square nib, matching the pencil.</summary>
    public void Paint(int direction, int frame, int x, int y, int size, bool dyeable)
    {
        var half = Math.Max(1, size) / 2;

        for (var dy = -half; dy <= half; dy++)
        for (var dx = -half; dx <= half; dx++)
        {
            if (Math.Max(1, size) % 2 == 0 && (dx == half || dy == half)) continue;
            Set(direction, frame, x + dx, y + dy, dyeable);
        }
    }

    /// <summary>
    /// Marks everything the part actually draws.
    ///
    /// The starting point, because a PCC part that dyes nowhere is the unusual one - the
    /// convention across every installed part is that the whole thing follows the
    /// character's colour, and exceptions are carved out of that rather than built up to.
    /// </summary>
    public void MarkAllDrawn(PccSheet sheet, byte alphaFloor = 1)
    {
        for (var direction = 0; direction < PccComposer.Rows; direction++)
        for (var frame = 0; frame < PccComposer.Columns; frame++)
        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
            Set(direction, frame, x, y, sheet.Get(direction, frame, x, y).A >= alphaFloor);
    }

    public void Clear() => Array.Clear(_dyeable);

    public byte[] Snapshot()
    {
        var bytes = new byte[_dyeable.Length];
        for (var i = 0; i < _dyeable.Length; i++) bytes[i] = _dyeable[i] ? (byte)1 : (byte)0;
        return bytes;
    }

    public void Restore(byte[] snapshot)
    {
        if (snapshot.Length != _dyeable.Length) return;
        for (var i = 0; i < _dyeable.Length; i++) _dyeable[i] = snapshot[i] != 0;
    }

    /// <summary>How many pixels of a cell will take the dye, for telling the user.</summary>
    public int CountIn(int direction, int frame)
    {
        var count = 0;

        for (var y = 0; y < CellHeight; y++)
        for (var x = 0; x < CellWidth; x++)
            if (Get(direction, frame, x, y)) count++;

        return count;
    }
}
