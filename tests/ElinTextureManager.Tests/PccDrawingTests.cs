using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The drawing tools a pixel artist expects, and the one thing they all have in common:
/// they stop at the edge of the cell they were used in. A sheet holds sixteen poses.
/// </summary>
public sealed class PccDrawingTests
{
    private static PccSheet Sheet() => PccSheet.Blank();

    private static int Opaque(PccSheet sheet)
    {
        var buffer = sheet.ToBuffer();
        var count = 0;
        for (var i = 3; i < buffer.Bgra.Length; i += 4) if (buffer.Bgra[i] != 0) count++;
        return count;
    }

    [Fact]
    public void A_horizontal_line_is_exactly_as_long_as_it_should_be()
    {
        var sheet = Sheet();

        sheet.DrawLine(0, 0, 4, 10, 12, 10, size: 1, 1, 2, 3, 255);

        Assert.Equal(9, Opaque(sheet));
        Assert.Equal((byte)255, sheet.Get(0, 0, 4, 10).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 12, 10).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 13, 10).A);
    }

    [Fact]
    public void A_diagonal_line_joins_its_ends_without_gaps()
    {
        var sheet = Sheet();

        sheet.DrawLine(0, 0, 2, 2, 10, 10, size: 1, 1, 2, 3, 255);

        // Nine steps on the diagonal, every one of them filled.
        for (var i = 0; i <= 8; i++)
            Assert.Equal((byte)255, sheet.Get(0, 0, 2 + i, 2 + i).A);
    }

    [Fact]
    public void A_line_drawn_backwards_is_the_same_line()
    {
        var forward = Sheet();
        var backward = Sheet();

        forward.DrawLine(0, 0, 3, 4, 20, 17, 1, 1, 2, 3, 255);
        backward.DrawLine(0, 0, 20, 17, 3, 4, 1, 1, 2, 3, 255);

        Assert.Equal(forward.ToBuffer().Bgra, backward.ToBuffer().Bgra);
    }

    [Fact]
    public void A_line_stops_at_the_edge_of_its_cell()
    {
        var sheet = Sheet();

        sheet.DrawLine(0, 0, 20, 10, 60, 10, size: 1, 1, 2, 3, 255);

        // The cell is 32 wide; the rest of that line would have run into the next frame.
        Assert.Equal(12, Opaque(sheet));
        Assert.Equal((byte)0, sheet.Get(0, 1, 0, 10).A);
    }

    [Fact]
    public void An_outlined_rectangle_is_hollow()
    {
        var sheet = Sheet();

        sheet.DrawRectangle(0, 0, 4, 4, 13, 13, size: 1, filled: false, 1, 2, 3, 255);

        Assert.Equal((byte)255, sheet.Get(0, 0, 4, 4).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 13, 13).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 8, 8).A);
    }

    [Fact]
    public void A_filled_rectangle_is_solid_and_the_right_size()
    {
        var sheet = Sheet();

        sheet.DrawRectangle(0, 0, 4, 4, 13, 9, size: 1, filled: true, 1, 2, 3, 255);

        Assert.Equal(10 * 6, Opaque(sheet));
        Assert.Equal((byte)255, sheet.Get(0, 0, 8, 7).A);
    }

    [Fact]
    public void A_rectangle_dragged_the_other_way_covers_the_same_ground()
    {
        var a = Sheet();
        var b = Sheet();

        a.DrawRectangle(0, 0, 4, 4, 13, 9, 1, true, 1, 2, 3, 255);
        b.DrawRectangle(0, 0, 13, 9, 4, 4, 1, true, 1, 2, 3, 255);

        Assert.Equal(a.ToBuffer().Bgra, b.ToBuffer().Bgra);
    }

    [Fact]
    public void Flipping_vertically_twice_puts_the_cell_back()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 5, 3, 9, 8, 7, 255);

        sheet.FlipCellVertically(0, 0);
        Assert.Equal((byte)255, sheet.Get(0, 0, 5, 44).A);

        sheet.FlipCellVertically(0, 0);
        Assert.Equal((byte)255, sheet.Get(0, 0, 5, 3).A);
    }

    [Fact]
    public void Replacing_a_colour_reaches_every_pixel_of_it_across_the_cell()
    {
        var sheet = Sheet();

        // Two separate patches of the same colour, with a gap between them: a flood
        // fill would only ever reach one.
        sheet.Set(0, 0, 2, 2, 10, 20, 30, 255);
        sheet.Set(0, 0, 25, 40, 10, 20, 30, 255);
        sheet.Set(0, 0, 10, 10, 99, 99, 99, 255);

        sheet.ReplaceColour(0, 0, (10, 20, 30, 255), 7, 7, 7, 255);

        Assert.Equal((byte)7, sheet.Get(0, 0, 2, 2).B);
        Assert.Equal((byte)7, sheet.Get(0, 0, 25, 40).B);
        Assert.Equal((byte)99, sheet.Get(0, 0, 10, 10).B);
    }

    [Fact]
    public void Replacing_a_colour_leaves_the_other_cells_alone()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 2, 2, 10, 20, 30, 255);
        sheet.Set(1, 0, 2, 2, 10, 20, 30, 255);

        sheet.ReplaceColour(0, 0, (10, 20, 30, 255), 7, 7, 7, 255);

        Assert.Equal((byte)10, sheet.Get(1, 0, 2, 2).B);
    }

    [Fact]
    public void The_sprites_own_palette_comes_back_most_used_first()
    {
        var sheet = Sheet();

        for (var i = 0; i < 5; i++) sheet.Set(0, 0, i, 0, 10, 10, 10, 255);
        for (var i = 0; i < 9; i++) sheet.Set(0, 0, i, 1, 20, 20, 20, 255);
        sheet.Set(0, 0, 0, 2, 30, 30, 30, 255);

        var palette = sheet.ColoursUsed();

        Assert.Equal(3, palette.Count);
        Assert.Equal((byte)20, palette[0].B);
        Assert.Equal((byte)10, palette[1].B);
        Assert.Equal((byte)30, palette[2].B);
    }

    [Fact]
    public void Transparent_pixels_are_not_a_colour_in_the_palette()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 1, 1, 10, 10, 10, 255);

        // The other 6,143 pixels are empty, and "nothing" is not a shade to paint with.
        Assert.Single(sheet.ColoursUsed());
    }

    [Fact]
    public void A_solid_ellipse_fills_its_middle_and_stops_at_its_corners()
    {
        var sheet = Sheet();

        sheet.DrawEllipse(0, 0, 4, 4, 20, 20, size: 1, filled: true, 1, 2, 3, 255);

        Assert.Equal((byte)255, sheet.Get(0, 0, 12, 12).A);

        // The corners of the box it was dragged in are outside the shape.
        Assert.Equal((byte)0, sheet.Get(0, 0, 4, 4).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 20, 20).A);
    }

    [Fact]
    public void An_outlined_ellipse_is_hollow()
    {
        var sheet = Sheet();

        sheet.DrawEllipse(0, 0, 2, 2, 22, 22, size: 1, filled: false, 1, 2, 3, 255);

        Assert.Equal((byte)0, sheet.Get(0, 0, 12, 12).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 12, 2).A);
    }

    [Fact]
    public void A_circle_is_round_rather_than_lopsided()
    {
        var sheet = Sheet();

        sheet.DrawEllipse(0, 0, 2, 2, 22, 22, 1, true, 1, 2, 3, 255);

        // The same distance from the centre in all four directions.
        Assert.Equal((byte)255, sheet.Get(0, 0, 12, 3).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 12, 21).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 3, 12).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 21, 12).A);
    }

    [Fact]
    public void An_ellipse_stays_inside_its_cell()
    {
        var sheet = Sheet();

        sheet.DrawEllipse(0, 0, 10, 10, 60, 60, 1, true, 1, 2, 3, 255);

        Assert.Equal((byte)0, sheet.Get(0, 1, 5, 5).A);
        Assert.Equal((byte)0, sheet.Get(1, 0, 5, 5).A);
    }

    [Fact]
    public void A_star_has_a_point_at_the_top()
    {
        var sheet = Sheet();

        sheet.DrawStar(0, 0, cx: 16, cy: 24, toX: 16, toY: 8, points: 5, size: 1,
            filled: false, 1, 2, 3, 255);

        // Dragged straight up, so the first point lands where the drag ended.
        Assert.Equal((byte)255, sheet.Get(0, 0, 16, 8).A);
    }

    [Fact]
    public void A_solid_star_is_filled_at_its_centre_and_empty_between_its_points()
    {
        var sheet = Sheet();

        sheet.DrawStar(0, 0, 16, 24, 16, 6, 5, 1, filled: true, 1, 2, 3, 255);

        Assert.Equal((byte)255, sheet.Get(0, 0, 16, 24).A);

        // Just outside the tip, in the notch between two points.
        Assert.Equal((byte)0, sheet.Get(0, 0, 2, 2).A);
    }

    [Fact]
    public void A_star_with_a_silly_number_of_points_is_brought_into_range()
    {
        var sheet = Sheet();

        // Would otherwise divide by zero or spin.
        sheet.DrawStar(0, 0, 16, 24, 16, 10, points: 0, size: 1, filled: false, 1, 2, 3, 255);
        sheet.DrawStar(0, 0, 16, 24, 16, 10, points: 99, size: 1, filled: false, 1, 2, 3, 255);

        Assert.True(Opaque(sheet) > 0);
    }

    [Fact]
    public void A_star_dragged_nowhere_still_draws_something_rather_than_nothing()
    {
        var sheet = Sheet();

        sheet.DrawStar(0, 0, 16, 24, 16, 24, 5, 1, false, 1, 2, 3, 255);

        Assert.True(Opaque(sheet) > 0);
    }
}
