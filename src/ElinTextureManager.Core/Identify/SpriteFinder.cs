namespace ElinTextureManager.Core.Identify;

/// <summary>
/// Finds which installed texture a cropped screenshot came from.
///
/// The ranking is by palette, which sounds crude and is not: a character's image and a
/// screenshot of that character share their colours even though they share no pixel
/// positions, and comparing 64 histogram bins across seven thousand files takes single
/// -digit milliseconds. On a real library this puts the right file first.
///
/// A second pass compares whole images at thumbnail size. It only ever promotes: when
/// the crop is the picture, that is worth saying outright, and when the crop is one
/// frame out of a sheet of twenty the comparison is meaningless and must not be allowed
/// to push the sheet down. An earlier version slid the crop over each candidate looking
/// for the frame; it cost fifteen seconds and moved the correct answer from first place
/// to forty-sixth, because a partial fit on the wrong sheet always beats a whole fit on
/// the right one.
///
/// Nothing here claims certainty. The output is a shortlist to look at - the last step,
/// is that the same character, is one a person does better and faster than any score.
/// </summary>
public sealed class SpriteFinder
{
    /// <summary>How many palette survivors get the whole-image comparison.</summary>
    public int RefineCount { get; init; } = 140;

    /// <summary>Side of the thumbnail both images are reduced to before comparing.</summary>
    public const int ThumbnailSide = 32;

    /// <summary>
    /// Mean per-channel difference below which two images are the same picture rather
    /// than merely similar. Rescaling and screenshot compression move a true match a few
    /// units off zero; different art of the same character sits far above this.
    /// </summary>
    public double StrongMatchThreshold { get; init; } = 26;

    /// <summary>
    /// Ranks every candidate by palette. Cheap enough to run over the whole library on
    /// every keystroke, which is why it is the primary ranking rather than a prefilter.
    /// </summary>
    public List<SpriteMatch> RankByPalette(
        SpriteSignature query,
        IEnumerable<(string Path, SpriteSignature Signature)> candidates)
    {
        var scored = new List<SpriteMatch>();

        foreach (var (path, signature) in candidates)
        {
            if (signature.SampleCount == 0) continue;

            scored.Add(new SpriteMatch
            {
                FullPath = path,
                PaletteScore = query.Covers(signature),
            });
        }

        scored.Sort((a, b) => b.PaletteScore.CompareTo(a.PaletteScore));
        return scored;
    }

    /// <summary>
    /// Compares two images whole, at <see cref="ThumbnailSide"/> square, ignoring aspect
    /// ratio because a screenshot crop is never framed exactly as the source file is.
    ///
    /// Returns the mean per-channel difference, 0 for identical.
    /// </summary>
    public static double WholeImageDifference(PixelBuffer query, PixelBuffer candidate)
    {
        if (query.IsEmpty || candidate.IsEmpty) return double.MaxValue;

        long sum = 0;
        var n = 0;

        for (var y = 0; y < ThumbnailSide; y++)
        for (var x = 0; x < ThumbnailSide; x++)
        {
            var ci = Sample(candidate, x, y);
            var qi = Sample(query, x, y);

            // Where the candidate draws nothing there is nothing to disagree about: the
            // crop's background belongs to the room, not to the character.
            if (candidate.Bgra[ci + 3] < 32) continue;

            sum += Math.Abs(query.Bgra[qi] - candidate.Bgra[ci])
                   + Math.Abs(query.Bgra[qi + 1] - candidate.Bgra[ci + 1])
                   + Math.Abs(query.Bgra[qi + 2] - candidate.Bgra[ci + 2]);
            n++;
        }

        return n < 16 ? double.MaxValue : sum / (double)(n * 3);
    }

    /// <summary>Nearest-neighbour sample of a normalised grid position.</summary>
    private static int Sample(PixelBuffer buffer, int x, int y)
    {
        var sx = Math.Min(buffer.Width - 1, x * buffer.Width / ThumbnailSide);
        var sy = Math.Min(buffer.Height - 1, y * buffer.Height / ThumbnailSide);
        return (sy * buffer.Width + sx) * 4;
    }

    /// <summary>
    /// Records the whole-image comparison on a match. Never reorders on its own - the
    /// caller decides, and <see cref="SpriteMatch.IsStrongMatch"/> is the only thing
    /// this promotes on.
    /// </summary>
    public void Refine(SpriteMatch match, PixelBuffer query, PixelBuffer candidate)
    {
        var difference = WholeImageDifference(query, candidate);

        match.PixelScore = difference == double.MaxValue ? null : difference;
        match.IsStrongMatch = difference <= StrongMatchThreshold;
    }

    /// <summary>
    /// Final order: whole-image matches first, best difference first, then everything
    /// else in palette order. A sheet the crop came one frame out of stays where the
    /// palette put it rather than being punished for not matching whole.
    /// </summary>
    public static List<SpriteMatch> Order(IEnumerable<SpriteMatch> matches) =>
        matches
            .OrderByDescending(m => m.IsStrongMatch)
            .ThenBy(m => m.IsStrongMatch ? m.PixelScore ?? double.MaxValue : 0)
            .ThenByDescending(m => m.PaletteScore)
            .ToList();
}
