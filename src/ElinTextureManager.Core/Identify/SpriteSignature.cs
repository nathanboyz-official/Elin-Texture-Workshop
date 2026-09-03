namespace ElinTextureManager.Core.Identify;

/// <summary>
/// A small, scale- and position-independent description of what colours an image is
/// made of.
///
/// This is the cheap first pass of the reverse lookup. A character's sprite sheet and
/// a screenshot of that character in play share a palette even though they share no
/// pixel positions: the sheet holds two dozen poses, and the screenshot holds one of
/// them at some unknown size against some unknown background. Comparing palettes
/// narrows thousands of candidates to a few dozen; comparing pixels is what settles it,
/// and that is expensive enough to be worth doing only on the few dozen.
/// </summary>
public sealed class SpriteSignature
{
    /// <summary>Bins per channel. 4 gives 64 bins - coarse enough to survive rescaling.</summary>
    public const int Bins = 4;

    public const int BinCount = Bins * Bins * Bins;

    /// <summary>Share of the image's own pixels in each bin, as 0-255.</summary>
    public required byte[] Histogram { get; init; }

    /// <summary>How many pixels went into it. Zero means the image was entirely transparent.</summary>
    public required int SampleCount { get; init; }

    /// <summary>
    /// Builds a signature from a BGRA buffer, counting only pixels the image actually
    /// draws. Transparent padding is most of a sprite sheet and describes nothing.
    /// </summary>
    public static SpriteSignature FromBgra(byte[] pixels, int width, int height, byte alphaFloor = 160)
    {
        var counts = new long[BinCount];
        var total = 0;

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < alphaFloor) continue;

            var b = pixels[i] * Bins / 256;
            var g = pixels[i + 1] * Bins / 256;
            var r = pixels[i + 2] * Bins / 256;

            counts[(r * Bins + g) * Bins + b]++;
            total++;
        }

        var hist = new byte[BinCount];
        if (total > 0)
        {
            for (var i = 0; i < BinCount; i++)
                hist[i] = (byte)Math.Min(255, counts[i] * 255 / total);
        }

        return new SpriteSignature { Histogram = hist, SampleCount = total };
    }

    /// <summary>
    /// How much of <paramref name="candidate"/>'s palette this one contains, from 0 to 1.
    ///
    /// One-directional: colours the crop has and the candidate does not are the room
    /// behind the character and must not count against it. Only the other direction -
    /// colours the candidate has and the crop lacks - is evidence.
    ///
    /// It compares proportions, which has a known cost: a crop that is half floor holds
    /// half the share of every colour the sprite is made of, so even a perfect match
    /// scores about one half. That looks like the thing to fix and is not. Relaxing it
    /// so a colour merely has to be present was measured against a real library of seven
    /// thousand images and moved the right answer from first place in 40 of 60 searches
    /// to 1 of 60, because once dilution is forgiven almost everything scores one and
    /// the ranking carries no information. Allowing a bounded amount - a crop up to two
    /// thirds background scoring full marks - landed in between at 34. Strict
    /// proportions win; the absolute score just should not be read as a probability.
    /// </summary>
    public double Covers(SpriteSignature candidate)
    {
        if (SampleCount == 0 || candidate.SampleCount == 0) return 0;

        var found = 0;
        var candidateTotal = 0;

        for (var i = 0; i < BinCount; i++)
        {
            var c = candidate.Histogram[i];
            if (c == 0) continue;

            candidateTotal += c;

            found += Math.Min(c, Histogram[i]);
        }

        return candidateTotal == 0 ? 0 : found / (double)candidateTotal;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[BinCount + 4];
        Histogram.CopyTo(buffer, 0);
        BitConverter.GetBytes(SampleCount).CopyTo(buffer, BinCount);
        return buffer;
    }

    public static SpriteSignature? FromBytes(byte[]? buffer)
    {
        if (buffer is null || buffer.Length != BinCount + 4) return null;

        return new SpriteSignature
        {
            Histogram = buffer[..BinCount],
            SampleCount = BitConverter.ToInt32(buffer, BinCount),
        };
    }
}
