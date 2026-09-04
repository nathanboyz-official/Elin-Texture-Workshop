using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Reading and writing the character styles Elin's own editor saves.
///
/// The format was taken from the user's real files rather than guessed, and these keep
/// it that way: a style written here has to be one the game's Import button accepts,
/// and a style read here has to survive being written back untouched.
/// </summary>
public sealed class PccStyleTests
{
    /// <summary>A named export, as the game's Export button writes one.</summary>
    private const string ExportedStyle = """
    {
      "$id": "1",
      "map": {
        "$id": "2",
        "body": [ "female", "2", "92847B" ],
        "undie": [ "female", "3", null ],
        "hair": [ "female", "8", null ],
        "cloth": [ "female", "43", "8A8F94" ]
      }
    }
    """;

    /// <summary>A favourite slot, which nests the same map one level deeper.</summary>
    private const string FavouriteStyle = """
    {
      "$id": "1",
      "data": {
        "$id": "2",
        "map": {
          "$id": "3",
          "body": [ "female", "2", "6F6E64" ],
          "hair": [ "female", "3", "7C8FAE" ]
        }
      }
    }
    """;

    [Fact]
    public void An_exported_style_is_read_layer_by_layer()
    {
        var style = PccStyleFile.Parse(ExportedStyle);

        Assert.NotNull(style);
        Assert.Equal(4, style!.Parts.Count);
        Assert.Equal("2", style.Get("body")!.Id);
        Assert.Equal("female", style.Get("body")!.Set);
        Assert.Equal("92847B", style.Get("body")!.Colour);
        Assert.Null(style.Get("hair")!.Colour);
    }

    [Fact]
    public void A_favourite_slot_nests_its_map_deeper_and_is_still_read()
    {
        // The five Register buttons write this shape; Export writes the other one.
        // Reading only one of them would mean half the user's saved characters are
        // invisible to us.
        var style = PccStyleFile.Parse(FavouriteStyle);

        Assert.NotNull(style);
        Assert.Equal("2", style!.Get("body")!.Id);
        Assert.Equal("7C8FAE", style.Get("hair")!.Colour);
    }

    [Fact]
    public void Unity_reference_bookkeeping_is_not_mistaken_for_a_layer()
    {
        var style = PccStyleFile.Parse(ExportedStyle)!;

        Assert.DoesNotContain(style.Parts.Keys, k => k.StartsWith('$'));
    }

    [Fact]
    public void What_is_written_reads_back_identically()
    {
        var original = PccStyleFile.Parse(ExportedStyle)!;

        var again = PccStyleFile.Parse(PccStyleFile.Write(original));

        Assert.NotNull(again);
        foreach (var (layer, choice) in original.Parts)
        {
            Assert.Equal(choice.Id, again!.Get(layer)!.Id);
            Assert.Equal(choice.Set, again.Get(layer)!.Set);
            Assert.Equal(choice.Colour, again.Get(layer)!.Colour);
        }
    }

    [Fact]
    public void A_favourite_written_as_a_favourite_keeps_its_nesting()
    {
        var style = PccStyleFile.Parse(FavouriteStyle)!;

        var written = PccStyleFile.Write(style, favouriteSlot: true);

        Assert.Contains("\"data\"", written);
        Assert.NotNull(PccStyleFile.Parse(written));
    }

    [Fact]
    public void Layers_the_editor_has_no_row_for_are_carried_through_rather_than_dropped()
    {
        // Two of this user's saved characters contain a "leg" layer from an older
        // version of the game. Saving one would otherwise quietly rewrite a character
        // they never asked to change.
        var style = PccStyleFile.Parse("""
        {"$id":"1","map":{"$id":"2","body":["female","2",null],"leg":["female","1.png",null]}}
        """)!;

        Assert.Contains("leg", PccStyleFile.Write(style));
        Assert.Equal("1.png", PccStyleFile.Parse(PccStyleFile.Write(style))!.Get("leg")!.Id);
    }

    [Fact]
    public void An_id_saved_with_a_file_extension_still_names_the_part()
    {
        // Older saves store "24.png" where newer ones store "24".
        Assert.Equal("24", new PccChoice { Id = "24.png" }.FileId);
        Assert.Equal("24", new PccChoice { Id = "24" }.FileId);
        Assert.Equal("KC0032", new PccChoice { Id = "KC0032" }.FileId);
    }

    [Fact]
    public void A_colour_is_read_as_rgb_and_a_missing_one_stays_missing()
    {
        Assert.Equal((byte)0x92, new PccChoice { Colour = "92847B" }.Rgb()!.Value.R);
        Assert.Equal((byte)0x84, new PccChoice { Colour = "92847B" }.Rgb()!.Value.G);
        Assert.Equal((byte)0x7B, new PccChoice { Colour = "92847B" }.Rgb()!.Value.B);

        Assert.Null(new PccChoice { Colour = null }.Rgb());
        Assert.Null(new PccChoice { Colour = "nonsense" }.Rgb());
    }

    [Fact]
    public void Nonsense_is_refused_rather_than_read_as_an_empty_character()
    {
        Assert.Null(PccStyleFile.Parse("not json"));
        Assert.Null(PccStyleFile.Parse("{}"));
        Assert.Null(PccStyleFile.Parse("""{"$id":"1","map":{"$id":"2"}}"""));
    }
}
