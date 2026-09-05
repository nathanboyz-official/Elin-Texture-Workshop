namespace ElinTextureManager.Core.Portraits;

/// <summary>
/// The shape a portrait is expected to be.
///
/// Measured from the game's own files rather than assumed: of the 473 portraits in
/// _Elona\Portrait, 447 are 240x320, and both of the examples Elin ships in
/// Custom\Portrait are the same. The rest are a handful of larger backgrounds and a few
/// small ones, so 240x320 is the size to aim at without being the only size that works -
/// the game scales whatever it is given.
/// </summary>
public static class PortraitSize
{
    public const int Width = 240;
    public const int Height = 320;

    public static bool IsStandard(int width, int height) =>
        width == Width && height == Height;

    /// <summary>
    /// What to say about an image of this size, or null when there is nothing worth
    /// saying. Only ever advice: an odd size still loads, it just gets stretched.
    /// </summary>
    public static string? Advice(int width, int height)
    {
        if (IsStandard(width, height)) return null;

        if (width <= 0 || height <= 0)
            return "That image has no size the reader could make sense of.";

        var same = Math.Abs((width / (double)height) - (Width / (double)Height)) < 0.01;

        return same
            ? $"That image is {width}x{height}. The shape is right, so scaling it to "
              + $"{Width}x{Height} will not distort it."
            : $"That image is {width}x{height}, not the usual {Width}x{Height}. The game "
              + "will stretch it to fit, which will squash the picture.";
    }
}
