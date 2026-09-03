using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Assembling a character out of PCC parts.
///
/// The library holds nearly three thousand of these and they are greyscale sheets that
/// Elin tints as it draws, so looking at one tells you almost nothing. Stacking them in
/// the documented order is what makes them legible.
/// </summary>
public sealed class PccTests
{
    /// <summary>A sheet of 4x4 cells, each cell filled with one flat colour.</summary>
    private static PixelBuffer Sheet(byte b, byte g, byte r, byte a = 255,
        int cellWidth = 32, int cellHeight = 48)
    {
        var w = cellWidth * PccComposer.Columns;
        var h = cellHeight * PccComposer.Rows;
        var px = new byte[w * h * 4];

        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
        }

        return new PixelBuffer { Bgra = px, Width = w, Height = h };
    }

    /// <summary>A sheet where every cell carries its own colour, to prove which was drawn.</summary>
    private static PixelBuffer NumberedSheet()
    {
        const int cw = 32, ch = 48;
        var w = cw * PccComposer.Columns;
        var h = ch * PccComposer.Rows;
        var px = new byte[w * h * 4];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            px[i] = (byte)(x / cw * 40);
            px[i + 1] = (byte)(y / ch * 40);
            px[i + 2] = 0;
            px[i + 3] = 255;
        }

        return new PixelBuffer { Bgra = px, Width = w, Height = h };
    }

    private static (byte B, byte G, byte R, byte A) At(PixelBuffer buffer, int x, int y)
    {
        var i = (y * buffer.Width + x) * 4;
        return (buffer.Bgra[i], buffer.Bgra[i + 1], buffer.Bgra[i + 2], buffer.Bgra[i + 3]);
    }

    [Fact]
    public void A_layer_is_read_from_the_file_name()
    {
        Assert.Equal("cloth", PccLayer.Of("pcc_cloth_mycoat.png"));
        Assert.Equal("body", PccLayer.Of("pcc_body_1.png"));
        Assert.Equal("face", PccLayer.Of("PCC_FACE_Cme100.PNG"));
        Assert.Null(PccLayer.Of("objC_2115.png"));
        Assert.Null(PccLayer.Of(null));
    }

    [Fact]
    public void Back_layers_are_not_mistaken_for_the_front_ones()
    {
        // "hair" is a prefix of "hairbk". Matching the short one first would put every
        // back-hair part in front of the face.
        Assert.Equal("hairbk", PccLayer.Of("pcc_hairbk_long.png"));
        Assert.Equal("mantlebk", PccLayer.Of("pcc_mantlebk_cape.png"));
        Assert.Equal("hair", PccLayer.Of("pcc_hair_long.png"));

        Assert.True(PccLayer.OrderOf("hairbk") < PccLayer.OrderOf("body"));
        Assert.True(PccLayer.OrderOf("body") < PccLayer.OrderOf("hair"));
    }

    [Fact]
    public void The_documented_draw_order_puts_the_body_under_its_clothes_and_face_on_top()
    {
        Assert.True(PccLayer.OrderOf("body") < PccLayer.OrderOf("cloth"));
        Assert.True(PccLayer.OrderOf("cloth") < PccLayer.OrderOf("face"));
        Assert.True(PccLayer.OrderOf("undie") < PccLayer.OrderOf("pants"));
        Assert.True(PccLayer.OrderOf("eye") < PccLayer.OrderOf("hair"));
    }

    [Fact]
    public void A_unique_id_is_the_part_after_the_layer()
    {
        Assert.Equal("mycoat", PccLayer.UniqueIdOf("pcc_cloth_mycoat.png"));
        Assert.Equal("1", PccLayer.UniqueIdOf("pcc_body_1.png"));

        // The documentation says an id must not contain underscores, which is only
        // checkable because the id is read back out rather than assumed.
        Assert.Contains('_', PccLayer.UniqueIdOf("pcc_cloth_my_coat.png")!);
    }

    [Fact]
    public void Pieces_are_drawn_back_to_front_whatever_order_they_arrive_in()
    {
        var body = new PccPiece { Layer = "body", Sheet = Sheet(10, 10, 200) };
        var cloth = new PccPiece { Layer = "cloth", Sheet = Sheet(200, 10, 10) };

        // Handed over in the wrong order on purpose: the layer decides, not the caller.
        var composed = PccComposer.Compose(new[] { cloth, body }, 0, 0, scale: 1);

        var pixel = At(composed, 16, 24);
        Assert.Equal((byte)200, pixel.B);
        Assert.Equal((byte)10, pixel.R);
    }

    [Fact]
    public void A_transparent_part_lets_what_is_under_it_show_through()
    {
        var body = new PccPiece { Layer = "body", Sheet = Sheet(0, 0, 255) };
        var invisible = new PccPiece { Layer = "cloth", Sheet = Sheet(0, 255, 0, a: 0) };

        var composed = PccComposer.Compose(new[] { body, invisible }, 0, 0, scale: 1);

        var pixel = At(composed, 16, 24);
        Assert.Equal((byte)255, pixel.R);
        Assert.Equal((byte)0, pixel.G);
        Assert.Equal((byte)255, pixel.A);
    }

    [Fact]
    public void A_half_transparent_part_blends_rather_than_replacing()
    {
        var body = new PccPiece { Layer = "body", Sheet = Sheet(0, 0, 0) };
        var veil = new PccPiece { Layer = "cloth", Sheet = Sheet(255, 255, 255, a: 128) };

        var composed = PccComposer.Compose(new[] { body, veil }, 0, 0, scale: 1);

        // Straight alpha, not premultiplied: half of white over black is grey, and a
        // premultiplied blend here would come out visibly darker.
        var pixel = At(composed, 16, 24);
        Assert.InRange(pixel.R, 120, 136);
        Assert.Equal((byte)255, pixel.A);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    public void The_asked_for_direction_and_frame_are_the_ones_drawn(int direction, int frame)
    {
        var piece = new PccPiece { Layer = "body", Sheet = NumberedSheet() };

        var composed = PccComposer.Compose(new[] { piece }, direction, frame, scale: 1);

        var pixel = At(composed, 16, 24);
        Assert.Equal((byte)(frame * 40), pixel.B);
        Assert.Equal((byte)(direction * 40), pixel.G);
    }

    [Fact]
    public void A_direction_or_frame_outside_the_sheet_is_clamped_rather_than_crashing()
    {
        var piece = new PccPiece { Layer = "body", Sheet = NumberedSheet() };

        var composed = PccComposer.Compose(new[] { piece }, direction: 99, frame: -5, scale: 1);

        Assert.Equal((byte)0, At(composed, 16, 24).B);
        Assert.Equal((byte)(3 * 40), At(composed, 16, 24).G);
    }

    [Fact]
    public void A_part_at_a_different_resolution_still_lines_up()
    {
        // Variable Sprite Support lets a part be any resolution so long as the sheet is
        // still four cells by four. A coat at double size has to land on the same body.
        var body = new PccPiece { Layer = "body", Sheet = Sheet(0, 0, 255) };
        var bigCloth = new PccPiece
        {
            Layer = "cloth",
            Sheet = Sheet(255, 0, 0, cellWidth: 64, cellHeight: 96),
        };

        var composed = PccComposer.Compose(new[] { body, bigCloth }, 0, 0, scale: 1);

        Assert.Equal(PccComposer.TileWidth, composed.Width);
        Assert.Equal((byte)255, At(composed, 16, 24).B);
    }

    [Fact]
    public void Scaling_up_keeps_the_pixels_square()
    {
        var piece = new PccPiece { Layer = "body", Sheet = NumberedSheet() };

        var composed = PccComposer.Compose(new[] { piece }, 0, 0, scale: 4);

        Assert.Equal(PccComposer.TileWidth * 4, composed.Width);
        Assert.Equal(PccComposer.TileHeight * 4, composed.Height);
    }

    [Fact]
    public void Parts_whose_layer_is_not_recognised_are_left_out_rather_than_stacked_wrongly()
    {
        var body = new PccPiece { Layer = "body", Sheet = Sheet(0, 0, 255) };
        var nonsense = new PccPiece { Layer = "sporran", Sheet = Sheet(0, 255, 0) };

        var composed = PccComposer.Compose(new[] { body, nonsense }, 0, 0, scale: 1);

        Assert.Equal((byte)255, At(composed, 16, 24).R);
        Assert.Equal((byte)0, At(composed, 16, 24).G);
    }

    [Fact]
    public void A_sheet_that_is_not_a_four_by_four_grid_is_reported_rather_than_drawn_wrong()
    {
        Assert.True(PccComposer.LooksLikeSheet(Sheet(0, 0, 0)));
        Assert.False(PccComposer.LooksLikeSheet(
            new PixelBuffer { Bgra = new byte[30 * 30 * 4], Width = 30, Height = 30 }));
    }

    [Fact]
    public void Nothing_selected_composes_an_empty_canvas_rather_than_failing()
    {
        var composed = PccComposer.Compose(Array.Empty<PccPiece>(), 0, 0, scale: 2);

        Assert.Equal(PccComposer.TileWidth * 2, composed.Width);
        Assert.Equal((byte)0, At(composed, 5, 5).A);
    }
}
