using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The slots the editor shows, and matching a saved character back to installed files.
/// </summary>
public sealed class PccLibraryTests
{
    private static PccLibrary Library()
    {
        var library = new PccLibrary();
        library.Add("body", "female", "1", @"C:\elin\pcc_body_1.png", "Elin");
        library.Add("body", "female", "2", @"C:\elin\pcc_body_2.png", "Elin");
        library.Add("hair", "female", "3", @"C:\elin\pcc_hair_3.png", "Elin");
        library.Add("hairbk", "female", "3", @"C:\elin\pcc_hairbk_3.png", "Elin");
        library.Add("hair", "female", "KC0021", @"C:\mods\pcc_hair_KC0021.png", "Kisekae");
        library.Add("cloth", "common", "9", @"C:\elin\common\pcc_cloth_9.png", "Elin");
        return library;
    }

    [Fact]
    public void The_slots_are_the_games_own_rows_in_the_games_own_order()
    {
        var labels = PccSlots.All.Select(s => s.Label).ToList();

        Assert.Equal(15, labels.Count);
        Assert.Equal("Body", labels[0]);
        Assert.Equal("Sub Hair", labels[4]);
        Assert.Equal("Foot", labels[^1]);
    }

    [Fact]
    public void The_labels_are_the_ones_the_game_ships()
    {
        // Taken from Elin's own localisation table, where pcc_mantle is "Back",
        // pcc_cloth is "Tops", pcc_belt is "Waist" and pcc_pants is "Bottoms".
        Assert.Equal("Back", PccSlots.For("mantle")!.Label);
        Assert.Equal("Tops", PccSlots.For("cloth")!.Label);
        Assert.Equal("Waist", PccSlots.For("belt")!.Label);
        Assert.Equal("Bottoms", PccSlots.For("pants")!.Label);
        Assert.Equal("Hand", PccSlots.For("glove")!.Label);
        Assert.Equal("Foot", PccSlots.For("boots")!.Label);
    }

    [Fact]
    public void The_two_back_layers_have_no_row_of_their_own()
    {
        // Seventeen layers, fifteen rows. hairbk and mantlebk are never chosen: the
        // game finds them by the same id as the front, which is why the front of one
        // hairstyle cannot be worn with the back of another.
        Assert.Null(PccSlots.For("hairbk"));
        Assert.Null(PccSlots.For("mantlebk"));
        Assert.True(PccSlots.IsBackLayer("hairbk"));
        Assert.True(PccSlots.IsBackLayer("mantlebk"));
        Assert.Equal("hairbk", PccSlots.BackLayerOf("hair"));
        Assert.Equal("mantlebk", PccSlots.BackLayerOf("mantle"));
    }

    [Fact]
    public void Choosing_a_hair_brings_its_back_piece_with_it()
    {
        var style = new PccStyle();
        style.Set("hair", new PccChoice { Id = "3" });

        var drawn = style.Drawable().ToList();

        Assert.Equal(2, drawn.Count);
        Assert.Contains(drawn, d => d.Layer == "hair" && d.Choice.Id == "3");
        Assert.Contains(drawn, d => d.Layer == "hairbk" && d.Choice.Id == "3");
    }

    [Fact]
    public void A_style_resolves_to_the_files_that_draw_it()
    {
        var style = new PccStyle();
        style.Set("body", new PccChoice { Id = "2" });
        style.Set("hair", new PccChoice { Id = "3" });

        var (found, missing) = Library().ResolveStyle(style);

        Assert.Empty(missing);
        Assert.Equal(3, found.Count);
        Assert.Contains(found, f => f.Part.FullPath.EndsWith("pcc_hairbk_3.png"));

        // Drawn back to front, so the body cannot land on top of the back hair.
        Assert.True(found.FindIndex(f => f.Layer == "hairbk")
                    < found.FindIndex(f => f.Layer == "body"));
    }

    [Fact]
    public void A_hair_with_no_back_piece_installed_simply_has_none()
    {
        var style = new PccStyle();
        style.Set("hair", new PccChoice { Id = "KC0021" });

        var (found, missing) = Library().ResolveStyle(style);

        // Most hairstyles have no back piece: 119 of 407 in a real library do. A
        // missing one is normal and must not be reported as a broken style.
        Assert.Empty(missing);
        Assert.Single(found);
    }

    [Fact]
    public void A_part_the_style_names_but_nobody_installed_is_reported()
    {
        var style = new PccStyle();
        style.Set("cloth", new PccChoice { Id = "nosuchthing" });

        var (found, missing) = Library().ResolveStyle(style);

        Assert.Empty(found);
        Assert.Contains(missing, m => m.Contains("nosuchthing"));
    }

    [Fact]
    public void A_part_installed_under_a_different_set_is_still_found()
    {
        // Styles always say "female", but mods put parts in common and unique too.
        var style = new PccStyle();
        style.Set("cloth", new PccChoice { Set = "female", Id = "9" });

        var (found, missing) = Library().ResolveStyle(style);

        Assert.Empty(missing);
        Assert.Single(found);
    }

    [Fact]
    public void A_new_character_gets_a_body_rather_than_an_empty_canvas()
    {
        Assert.NotNull(Library().DefaultBody());
        Assert.Equal("body", Library().DefaultBody()!.Layer);
    }

    [Fact]
    public void Clearing_a_slot_removes_it_rather_than_storing_an_empty_choice()
    {
        var style = new PccStyle();
        style.Set("hair", new PccChoice { Id = "3" });
        style.Set("hair", null);

        Assert.Empty(style.Parts);
        Assert.DoesNotContain("hair", PccStyleFile.Write(style));
    }

    [Fact]
    public void A_copied_style_does_not_share_its_choices()
    {
        var style = new PccStyle();
        style.Set("hair", new PccChoice { Id = "3", Colour = "FFFFFF" });

        var copy = style.Copy();
        copy.Get("hair")!.Colour = "000000";

        Assert.Equal("FFFFFF", style.Get("hair")!.Colour);
    }

    [Fact]
    public void A_dyed_part_comes_out_the_colour_that_was_chosen()
    {
        // Parts are drawn around mid grey so dyeing can go both up and down from there.
        // A neutral pixel must come out as exactly the chosen colour - treating white
        // as neutral instead renders every character about half as bright.
        var sheet = new PixelBuffer
        {
            Bgra = Tile(new byte[] { 128, 128, 128, 255 }), Width = 128, Height = 192,
        };

        var piece = new PccPiece { Layer = "body", Sheet = sheet, Tint = (200, 100, 50) };
        var composed = PccComposer.Compose(new[] { piece }, 0, 0, scale: 1);

        var i = (24 * composed.Width + 16) * 4;
        Assert.InRange(composed.Bgra[i], 48, 52);
        Assert.InRange(composed.Bgra[i + 1], 98, 102);
        Assert.InRange(composed.Bgra[i + 2], 198, 202);
    }

    [Fact]
    public void An_undyed_part_is_drawn_exactly_as_it_was_authored()
    {
        var sheet = new PixelBuffer
        {
            Bgra = Tile(new byte[] { 90, 140, 210, 255 }), Width = 128, Height = 192,
        };

        var piece = new PccPiece { Layer = "body", Sheet = sheet, Tint = null };
        var composed = PccComposer.Compose(new[] { piece }, 0, 0, scale: 1);

        var i = (24 * composed.Width + 16) * 4;
        Assert.Equal(90, composed.Bgra[i]);
        Assert.Equal(140, composed.Bgra[i + 1]);
        Assert.Equal(210, composed.Bgra[i + 2]);
    }

    private static byte[] Tile(byte[] pixel)
    {
        var px = new byte[128 * 192 * 4];
        for (var i = 0; i < px.Length; i += 4) pixel.CopyTo(px, i);
        return px;
    }
}
