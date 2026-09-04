using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The rules a saved part's name has to follow to be loadable.
///
/// Not house style. The game reads the layer and the id out of the file name by
/// splitting on underscores, so a name that breaks the rule produces a file that simply
/// never appears in game - the worst kind of failure, because nothing reports it.
/// </summary>
public sealed class PccPartNameTests
{
    [Fact]
    public void A_saved_part_is_named_the_way_the_game_reads_one()
    {
        Assert.Equal("pcc_hair_mine.png", PccPartName.FileName("hair", "mine"));
    }

    [Fact]
    public void An_underscore_in_the_name_is_refused_with_the_reason()
    {
        var check = PccPartName.Check("hair", "my_hair");

        Assert.False(check.Ok);
        Assert.Contains("underscore", check.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("mine")]
    [InlineData("KC0021")]
    [InlineData("cme50")]
    [InlineData("kitsune-1-3b")]
    public void Names_that_look_like_the_ones_already_installed_are_fine(string id)
    {
        Assert.True(PccPartName.Check("hair", id).Ok);
    }

    [Fact]
    public void An_empty_name_or_an_unknown_layer_is_refused()
    {
        Assert.False(PccPartName.Check("hair", "").Ok);
        Assert.False(PccPartName.Check("hair", "   ").Ok);
        Assert.False(PccPartName.Check("sporran", "mine").Ok);
        Assert.False(PccPartName.Check(null, "mine").Ok);
    }

    [Fact]
    public void A_dot_is_refused_rather_than_producing_a_double_extension()
    {
        Assert.False(PccPartName.Check("hair", "mine.png").Ok);
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_refused()
    {
        Assert.False(PccPartName.Check("hair", "a/b").Ok);
        Assert.False(PccPartName.Check("hair", "a:b").Ok);
    }

    [Fact]
    public void A_copy_gets_a_free_name_rather_than_overwriting_the_original()
    {
        var used = new HashSet<string> { "cme50copy", "cme50copy2" };

        Assert.Equal("cme50copy", PccPartName.Available("cme50", _ => false));
        Assert.Equal("cme50copy3", PccPartName.Available("cme50", used.Contains));
    }

    [Fact]
    public void A_sprite_started_from_nothing_is_not_called_a_copy()
    {
        // Nothing was copied, and the file name would carry that small lie forever.
        Assert.Equal("mycloth", PccPartName.Available("mycloth", _ => false, suffix: ""));
        Assert.Equal("mycloth2",
            PccPartName.Available("mycloth", n => n == "mycloth", suffix: ""));
    }

    [Fact]
    public void A_copy_of_a_part_whose_name_breaks_the_rule_gets_a_name_that_does_not()
    {
        // The library contains parts with underscores and dots in their ids already;
        // copying one must not carry the problem into the new file.
        var suggested = PccPartName.Available("my_odd.name", _ => false);

        Assert.DoesNotContain('_', suggested);
        Assert.DoesNotContain('.', suggested);
        Assert.True(PccPartName.Check("hair", suggested).Ok);
    }
}
