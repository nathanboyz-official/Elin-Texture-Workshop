using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Choosing which pixels of a part take the character's dye.
///
/// The game multiplies a whole part by one colour, so a grey pixel comes out as whatever
/// the character wears and a coloured one keeps its own hue through the multiply. Paint
/// in colour, then say which of it should follow the character.
/// </summary>
public sealed class PccDyeMaskTests
{
    private static (PccSheet Sheet, PccDyeMask Mask) Part()
    {
        var sheet = PccSheet.Blank();
        var mask = new PccDyeMask(sheet.Width, sheet.Height);
        return (sheet, mask);
    }

    [Fact]
    public void Marking_starts_from_everything_the_part_draws()
    {
        var (sheet, mask) = Part();
        sheet.Set(0, 0, 5, 5, 10, 20, 200, 255);
        sheet.Set(1, 2, 6, 6, 10, 20, 200, 255);

        mask.MarkAllDrawn(sheet);

        // Every installed part dyes throughout; exceptions are carved out of that
        // rather than built up to.
        Assert.True(mask.Get(0, 0, 5, 5));
        Assert.True(mask.Get(1, 2, 6, 6));
        Assert.False(mask.Get(0, 0, 9, 9));
    }

    [Fact]
    public void Marking_one_cell_leaves_the_others_alone()
    {
        var (_, mask) = Part();

        mask.Paint(2, 1, 10, 10, size: 1, dyeable: true);

        Assert.True(mask.Get(2, 1, 10, 10));
        Assert.False(mask.Get(0, 0, 10, 10));
        Assert.Equal(1, mask.CountIn(2, 1));
        Assert.Equal(0, mask.CountIn(0, 0));
    }

    [Fact]
    public void A_marked_pixel_is_written_as_grey_and_keeps_its_brightness()
    {
        var (sheet, mask) = Part();

        // A mid green: brighter than its red or blue channels would suggest.
        sheet.Set(0, 0, 4, 4, 40, 160, 60, 255);
        mask.Set(0, 0, 4, 4, true);

        var flat = PccFlatten.Apply(sheet, mask);
        var i = (4 * flat.Width + 4) * 4;

        var expected = PccFlatten.Luminance(60, 160, 40);
        Assert.Equal(expected, flat.Bgra[i]);
        Assert.Equal(expected, flat.Bgra[i + 1]);
        Assert.Equal(expected, flat.Bgra[i + 2]);
        Assert.Equal(255, flat.Bgra[i + 3]);
    }

    [Fact]
    public void An_unmarked_pixel_keeps_exactly_the_colour_it_was_painted()
    {
        var (sheet, mask) = Part();
        sheet.Set(0, 0, 4, 4, 40, 160, 60, 255);

        var flat = PccFlatten.Apply(sheet, mask);
        var i = (4 * flat.Width + 4) * 4;

        Assert.Equal(40, flat.Bgra[i]);
        Assert.Equal(160, flat.Bgra[i + 1]);
        Assert.Equal(60, flat.Bgra[i + 2]);
    }

    [Fact]
    public void Brightness_is_weighted_the_way_the_eye_weighs_it()
    {
        // Otherwise a green and a blue of the same measured value flatten to the same
        // grey, and the shading an artist drew comes apart.
        Assert.True(PccFlatten.Luminance(0, 255, 0) > PccFlatten.Luminance(255, 0, 0));
        Assert.True(PccFlatten.Luminance(255, 0, 0) > PccFlatten.Luminance(0, 0, 255));
        Assert.Equal(255, PccFlatten.Luminance(255, 255, 255));
        Assert.Equal(0, PccFlatten.Luminance(0, 0, 0));
    }

    [Fact]
    public void Transparent_pixels_stay_transparent_whatever_the_mask_says()
    {
        var (sheet, mask) = Part();
        mask.Set(0, 0, 4, 4, true);

        var flat = PccFlatten.Apply(sheet, mask);

        Assert.Equal(0, flat.Bgra[(4 * flat.Width + 4) * 4 + 3]);
    }

    [Fact]
    public void The_preview_shows_marked_pixels_red_and_the_rest_grey()
    {
        var (sheet, mask) = Part();
        sheet.Set(0, 0, 4, 4, 200, 200, 200, 255);
        sheet.Set(0, 0, 6, 6, 200, 200, 200, 255);
        mask.Set(0, 0, 4, 4, true);

        var preview = PccFlatten.Preview(sheet, mask, 0, 0);

        var marked = (4 * preview.Width + 4) * 4;
        var plain = (6 * preview.Width + 6) * 4;

        // Red reads clearly above its own blue and green.
        Assert.True(preview.Bgra[marked + 2] > preview.Bgra[marked] + 60);

        // And an unmarked pixel is a neutral grey.
        Assert.Equal(preview.Bgra[plain], preview.Bgra[plain + 1]);
        Assert.Equal(preview.Bgra[plain + 1], preview.Bgra[plain + 2]);
    }

    [Fact]
    public void An_undo_snapshot_puts_the_marking_back()
    {
        var (sheet, mask) = Part();
        sheet.Set(0, 0, 4, 4, 1, 2, 3, 255);
        mask.MarkAllDrawn(sheet);

        var before = mask.Snapshot();
        mask.Clear();
        mask.Restore(before);

        Assert.True(mask.Get(0, 0, 4, 4));
    }
}
