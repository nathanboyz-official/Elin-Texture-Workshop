using ElinTextureManager.Core.Identify;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The reverse lookup: given a crop from a screenshot, which installed image is it?
///
/// These work on hand-built pixel buffers rather than real files, which is the point of
/// keeping the matching in Core: it is arithmetic over byte arrays and needs no decoder,
/// no library and no game installed to be pinned down.
/// </summary>
public sealed class IdentifyTests
{
    /// <summary>A solid rectangle of one colour, fully opaque.</summary>
    private static PixelBuffer Solid(int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
        }
        return new PixelBuffer { Bgra = px, Width = w, Height = h };
    }

    /// <summary>Two vertical bands, which is enough structure to tell images apart.</summary>
    private static PixelBuffer Banded(int w, int h,
        (byte B, byte G, byte R) left, (byte B, byte G, byte R) right, byte alpha = 255)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = x < w / 2 ? left : right;
            var i = (y * w + x) * 4;
            px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = alpha;
        }
        return new PixelBuffer { Bgra = px, Width = w, Height = h };
    }

    private static SpriteSignature Sign(PixelBuffer b, byte floor = 160) =>
        SpriteSignature.FromBgra(b.Bgra, b.Width, b.Height, floor);

    [Fact]
    public void Transparent_pixels_are_not_part_of_an_images_palette()
    {
        // A sprite sheet is mostly empty space. Counting it would make every sheet look
        // like every other sheet.
        var sheet = Solid(32, 32, 200, 60, 60, a: 0);

        Assert.Equal(0, Sign(sheet).SampleCount);
    }

    [Fact]
    public void The_same_image_at_a_different_size_has_the_same_palette()
    {
        var small = Banded(16, 16, (200, 60, 60), (30, 30, 200));
        var large = Banded(64, 64, (200, 60, 60), (30, 30, 200));

        Assert.Equal(1.0, Sign(small).Covers(Sign(large)), 3);
    }

    [Fact]
    public void A_crop_that_is_half_background_scores_about_half()
    {
        // Not a bug to be fixed - a documented cost. Coverage compares proportions, so
        // the room behind the character dilutes the sprite's own colours. Forgiving that
        // dilution was measured and made the ranking far worse; see Covers.
        var sprite = Solid(16, 16, 200, 60, 60);
        var crop = Banded(32, 16, (200, 60, 60), (40, 70, 45));

        Assert.Equal(0.5, Sign(crop).Covers(Sign(sprite)), 2);
    }

    [Fact]
    public void The_sprites_own_colours_still_beat_colours_that_are_absent()
    {
        // What actually matters is the ordering, which dilution leaves intact: every
        // candidate is diluted by the same background.
        var crop = Banded(32, 16, (200, 60, 60), (40, 70, 45));

        var present = Sign(crop).Covers(Sign(Solid(16, 16, 200, 60, 60)));
        var absent = Sign(crop).Covers(Sign(Solid(16, 16, 20, 220, 30)));

        Assert.True(present > absent, $"{present} should beat {absent}");
    }

    [Fact]
    public void An_image_made_of_colours_the_crop_does_not_have_scores_zero()
    {
        var crop = Solid(16, 16, 200, 60, 60);
        var unrelated = Solid(16, 16, 20, 220, 30);

        Assert.Equal(0, Sign(crop).Covers(Sign(unrelated)));
    }

    [Fact]
    public void Palette_ranking_puts_the_source_image_first()
    {
        var crop = Banded(40, 40, (210, 70, 70), (40, 70, 45));

        var candidates = new[]
        {
            ("truth", Sign(Banded(20, 20, (210, 70, 70), (40, 70, 45)))),
            ("green", Sign(Solid(20, 20, 20, 220, 30))),
            ("blue", Sign(Solid(20, 20, 220, 40, 20))),
        };

        var ranked = new SpriteFinder().RankByPalette(Sign(crop, floor: 1), candidates);

        Assert.Equal("truth", ranked[0].FullPath);
    }

    [Fact]
    public void Candidates_with_nothing_drawn_are_left_out_of_the_ranking()
    {
        var crop = Solid(16, 16, 200, 60, 60);
        var candidates = new[]
        {
            ("empty", Sign(Solid(16, 16, 200, 60, 60, a: 0))),
            ("real", Sign(Solid(16, 16, 200, 60, 60))),
        };

        var ranked = new SpriteFinder().RankByPalette(Sign(crop), candidates);

        Assert.Single(ranked);
        Assert.Equal("real", ranked[0].FullPath);
    }

    [Fact]
    public void The_same_picture_rescaled_is_recognised_as_the_same_picture()
    {
        var source = Banded(24, 24, (210, 70, 70), (60, 60, 210));
        var crop = Banded(53, 53, (210, 70, 70), (60, 60, 210));

        var difference = SpriteFinder.WholeImageDifference(crop, source);

        Assert.True(difference < new SpriteFinder().StrongMatchThreshold,
            $"difference was {difference}");
    }

    [Fact]
    public void A_different_picture_is_not_called_the_same_picture()
    {
        var source = Banded(24, 24, (210, 70, 70), (60, 60, 210));
        var other = Banded(24, 24, (60, 60, 210), (210, 70, 70));

        var difference = SpriteFinder.WholeImageDifference(other, source);

        Assert.True(difference > new SpriteFinder().StrongMatchThreshold,
            $"difference was {difference}");
    }

    [Fact]
    public void Where_the_candidate_draws_nothing_the_crops_background_is_not_held_against_it()
    {
        // The candidate is a sprite on transparency; the crop has a room behind it. The
        // room must not count as disagreement, or every crop would fail.
        var sprite = Solid(24, 24, 200, 60, 60, a: 0);
        for (var i = 0; i < 24 * 12 * 4; i += 4) sprite.Bgra[i + 3] = 255;

        var crop = Solid(24, 24, 200, 60, 60);
        for (var i = 24 * 12 * 4; i < crop.Bgra.Length; i += 4)
        {
            crop.Bgra[i] = 40; crop.Bgra[i + 1] = 70; crop.Bgra[i + 2] = 45;
        }

        Assert.Equal(0, SpriteFinder.WholeImageDifference(crop, sprite), 3);
    }

    [Fact]
    public void A_whole_image_match_outranks_a_better_palette_score()
    {
        var strong = new SpriteMatch
        {
            FullPath = "strong", PaletteScore = 0.4, IsStrongMatch = true, PixelScore = 3,
        };
        var colourful = new SpriteMatch { FullPath = "colourful", PaletteScore = 0.95 };

        var ordered = SpriteFinder.Order(new[] { colourful, strong });

        Assert.Equal("strong", ordered[0].FullPath);
        Assert.Equal("colourful", ordered[1].FullPath);
    }

    [Fact]
    public void Sheets_the_crop_came_one_frame_out_of_keep_their_palette_order()
    {
        // None of these matched whole, which is what happens when the crop is one pose
        // from a sheet of twenty. Demoting them for it would throw away the only signal
        // there was.
        var a = new SpriteMatch { FullPath = "a", PaletteScore = 0.9, PixelScore = 120 };
        var b = new SpriteMatch { FullPath = "b", PaletteScore = 0.7, PixelScore = 40 };

        var ordered = SpriteFinder.Order(new[] { b, a });

        Assert.Equal("a", ordered[0].FullPath);
    }

    [Fact]
    public void A_signature_survives_the_round_trip_through_the_cache()
    {
        var original = Sign(Banded(24, 24, (210, 70, 70), (60, 60, 210)));

        var restored = SpriteSignature.FromBytes(original.ToBytes());

        Assert.NotNull(restored);
        Assert.Equal(original.SampleCount, restored!.SampleCount);
        Assert.Equal(original.Histogram, restored.Histogram);
        Assert.Equal(1.0, restored.Covers(original), 3);
    }

    [Fact]
    public void A_blob_of_the_wrong_shape_is_rejected_rather_than_misread()
    {
        Assert.Null(SpriteSignature.FromBytes(null));
        Assert.Null(SpriteSignature.FromBytes(Array.Empty<byte>()));
        Assert.Null(SpriteSignature.FromBytes(new byte[SpriteSignature.BinCount]));
    }
}
