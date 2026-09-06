namespace ElinTextureManager.Core.Identify;

/// <summary>A decoded image, as the matcher wants it: straight BGRA, no framework types.</summary>
public sealed class PixelBuffer
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>One candidate, scored.</summary>
public sealed class SpriteMatch
{
    public required string FullPath { get; init; }

    /// <summary>How much of this candidate's palette the crop contains, 0-1.</summary>
    public required double PaletteScore { get; init; }

    /// <summary>
    /// Mean per-channel difference between the two images compared whole, or null when
    /// the comparison did not run or had too little to compare.
    /// </summary>
    public double? PixelScore { get; set; }

    /// <summary>Set when the crop is this picture, rather than merely sharing its colours.</summary>
    public bool IsStrongMatch { get; set; }

    /// <summary>
    /// Words rather than a percentage. The palette score is a ranking key, not a
    /// probability - a crop with a lot of room in it scores low even when it is the
    /// right answer - and putting a number on it would invite reading it as confidence.
    /// The thresholds are set from where real matches actually land.
    /// </summary>
    public string ConfidenceLabel => IsStrongMatch
        ? "same image"
        : PaletteScore >= 0.7 ? "close colours"
        : PaletteScore >= 0.45 ? "similar colours"
        : "possible";
}
