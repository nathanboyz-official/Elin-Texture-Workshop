namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// Which way a character faces, and what turning does.
///
/// The four rows of a PCC sheet are stored front, left, right, back - which is the order
/// they sit in the file, not the order you meet them walking round someone. Stepping
/// through the rows makes a character flip from one profile straight to the other, a
/// half turn in a single click. Turning has to follow the circle instead: front, left,
/// back, right, and round again.
/// </summary>
public static class PccFacing
{
    public const int Front = 0;
    public const int Left = 1;
    public const int Right = 2;
    public const int Back = 3;

    /// <summary>The rows in the order a body actually passes through them turning on the spot.</summary>
    public static IReadOnlyList<int> Circle { get; } = new[] { Front, Left, Back, Right };

    public static string NameOf(int facing) => facing switch
    {
        Left => "Left",
        Right => "Right",
        Back => "Back",
        _ => "Front",
    };

    /// <summary>
    /// Turns from one facing by a number of quarter turns, either way, wrapping round.
    /// </summary>
    public static int Turn(int facing, int quarterTurns)
    {
        var at = ((IList<int>)Circle).IndexOf(facing);
        if (at < 0) at = 0;

        var count = Circle.Count;
        var next = ((at + quarterTurns) % count + count) % count;

        return Circle[next];
    }
}
