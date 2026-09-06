using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Editing a PCC sheet.
///
/// Every operation is cell-local, and that is the thing worth testing: a sheet holds
/// sixteen poses, and an edit that leaks past the cell it was made in repaints fifteen
/// drawings nobody touched.
/// </summary>
public sealed class PccSheetTests
{
    private static PccSheet Sheet() => PccSheet.Blank();

    private static int Opaque(PccSheet sheet)
    {
        var buffer = sheet.ToBuffer();
        var count = 0;

        for (var i = 3; i < buffer.Bgra.Length; i += 4)
            if (buffer.Bgra[i] != 0) count++;

        return count;
    }

    [Fact]
    public void A_blank_sheet_is_the_size_the_games_own_parts_are()
    {
        var sheet = Sheet();

        // 4 frames across, 4 facings down, 32x48 each.
        Assert.Equal(128, sheet.Width);
        Assert.Equal(192, sheet.Height);
        Assert.Equal(32, sheet.CellWidth);
        Assert.Equal(48, sheet.CellHeight);
        Assert.Equal(0, Opaque(sheet));
    }

    [Fact]
    public void Drawing_in_one_cell_leaves_the_other_fifteen_alone()
    {
        var sheet = Sheet();

        sheet.Draw(direction: 2, frame: 1, x: 10, y: 20, size: 1, 10, 20, 30, 255);

        Assert.Equal((byte)255, sheet.Get(2, 1, 10, 20).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 10, 20).A);
        Assert.Equal((byte)0, sheet.Get(2, 0, 10, 20).A);
        Assert.Equal(1, Opaque(sheet));
    }

    [Fact]
    public void Drawing_outside_a_cell_is_dropped_rather_than_spilling_into_the_next_one()
    {
        var sheet = Sheet();

        // One pixel past the right edge of the cell would be the next frame along.
        sheet.Draw(0, 0, x: 32, y: 10, size: 1, 10, 20, 30, 255);
        sheet.Draw(0, 0, x: -1, y: 10, size: 1, 10, 20, 30, 255);
        sheet.Draw(0, 0, x: 10, y: 48, size: 1, 10, 20, 30, 255);

        Assert.Equal(0, Opaque(sheet));
    }

    [Fact]
    public void A_bigger_nib_paints_a_square_around_the_point()
    {
        var sheet = Sheet();

        sheet.Draw(0, 0, 10, 10, size: 3, 10, 20, 30, 255);

        Assert.Equal(9, Opaque(sheet));
        Assert.Equal((byte)255, sheet.Get(0, 0, 9, 9).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 11, 11).A);
    }

    [Fact]
    public void A_fill_stops_at_the_edge_of_its_cell()
    {
        var sheet = Sheet();

        sheet.Fill(1, 1, 5, 5, 10, 20, 30, 255);

        // The whole cell, and only that cell.
        Assert.Equal(32 * 48, Opaque(sheet));
        Assert.Equal((byte)0, sheet.Get(0, 0, 5, 5).A);
    }

    [Fact]
    public void A_fill_does_not_cross_a_line_of_another_colour()
    {
        var sheet = Sheet();

        for (var y = 0; y < 48; y++) sheet.Set(0, 0, 16, y, 0, 0, 0, 255);

        sheet.Fill(0, 0, 5, 5, 10, 20, 30, 255);

        Assert.Equal((byte)255, sheet.Get(0, 0, 5, 5).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 20, 5).A);
    }

    [Fact]
    public void Filling_with_the_colour_that_is_already_there_does_nothing_rather_than_hanging()
    {
        var sheet = Sheet();
        sheet.Fill(0, 0, 5, 5, 0, 0, 0, 0);

        Assert.Equal(0, Opaque(sheet));
    }

    [Fact]
    public void Mirroring_flips_one_cell_and_leaves_the_rest()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 2, 10, 1, 2, 3, 255);
        sheet.Set(1, 0, 2, 10, 1, 2, 3, 255);

        sheet.MirrorCell(0, 0);

        Assert.Equal((byte)0, sheet.Get(0, 0, 2, 10).A);
        Assert.Equal((byte)255, sheet.Get(0, 0, 29, 10).A);
        Assert.Equal((byte)255, sheet.Get(1, 0, 2, 10).A);
    }

    [Fact]
    public void Mirroring_twice_puts_it_back()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 4, 7, 9, 8, 7, 255);

        sheet.MirrorCell(0, 0);
        sheet.MirrorCell(0, 0);

        Assert.Equal((byte)255, sheet.Get(0, 0, 4, 7).A);
    }

    [Fact]
    public void Nudging_moves_the_drawing_and_drops_what_goes_over_the_edge()
    {
        var sheet = Sheet();
        sheet.Set(0, 0, 5, 5, 1, 2, 3, 255);
        sheet.Set(0, 0, 0, 5, 1, 2, 3, 255);

        sheet.NudgeCell(0, 0, dx: -1, dy: 0);

        Assert.Equal((byte)255, sheet.Get(0, 0, 4, 5).A);
        Assert.Equal((byte)0, sheet.Get(0, 0, 5, 5).A);

        // The one that was already at the edge has left the sprite.
        Assert.Equal(1, Opaque(sheet));
    }

    [Fact]
    public void Copying_a_cell_replaces_the_target_and_leaves_the_source()
    {
        var sheet = Sheet();
        sheet.Fill(0, 0, 5, 5, 10, 20, 30, 255);
        sheet.Set(0, 1, 5, 5, 99, 99, 99, 255);

        sheet.CopyCell(0, 0, 0, 1);

        Assert.Equal((byte)10, sheet.Get(0, 1, 5, 5).B);
        Assert.Equal(32 * 48 * 2, Opaque(sheet));
    }

    [Fact]
    public void Clearing_empties_only_the_cell_asked_for()
    {
        var sheet = Sheet();
        sheet.Fill(0, 0, 5, 5, 10, 20, 30, 255);
        sheet.Fill(0, 1, 5, 5, 10, 20, 30, 255);

        sheet.ClearCell(0, 0);

        Assert.Equal(32 * 48, Opaque(sheet));
        Assert.Equal((byte)255, sheet.Get(0, 1, 5, 5).A);
    }

    [Fact]
    public void An_undo_snapshot_puts_every_pixel_back()
    {
        var sheet = Sheet();
        sheet.Fill(0, 0, 5, 5, 10, 20, 30, 255);

        var before = sheet.Snapshot();
        sheet.Fill(0, 0, 5, 5, 99, 88, 77, 255);
        sheet.NudgeCell(0, 0, 3, 3);
        sheet.Restore(before);

        Assert.Equal((byte)10, sheet.Get(0, 0, 5, 5).B);
        Assert.Equal(32 * 48, Opaque(sheet));
    }

    [Fact]
    public void Editing_a_loaded_part_never_writes_through_to_the_original()
    {
        var original = new PixelBuffer { Bgra = new byte[128 * 192 * 4], Width = 128, Height = 192 };

        var sheet = PccSheet.From(original)!;
        sheet.Fill(0, 0, 1, 1, 5, 5, 5, 255);

        Assert.Equal(0, original.Bgra[3]);
    }

    [Fact]
    public void Something_that_is_not_a_sheet_is_refused()
    {
        var odd = new PixelBuffer { Bgra = new byte[30 * 30 * 4], Width = 30, Height = 30 };

        Assert.Null(PccSheet.From(odd));
    }
}
