using ElinTextureManager.Core.Portraits;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The rules for adding a portrait of your own to Elin\Custom\Portrait.
///
/// The folder is additive - the game offers what it finds there alongside its own - so
/// the one thing that must never happen is a new portrait landing on top of a portrait
/// already in it. The "-overlay" ending matters for the same reason: Elin reads
/// "x-overlay.png" as a layer painted over "x.png", so a name that ends that way is a
/// portrait that never shows up on its own.
/// </summary>
public sealed class PortraitNameTests
{
    [Fact]
    public void A_portrait_is_named_the_way_the_game_reads_one()
    {
        Assert.Equal("my cat.png", PortraitName.FileName("my cat"));
    }

    [Fact]
    public void The_name_is_taken_from_the_file_the_user_picked()
    {
        Assert.Equal("My Cat", PortraitName.FromFile(@"C:\pics\My Cat.png"));
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_dropped()
    {
        Assert.Equal("My Cat 2", PortraitName.FromFile(@"C:\pics\My: Cat ""2"".png"));
    }

    [Fact]
    public void A_file_with_no_usable_name_still_gets_one()
    {
        Assert.Equal("portrait", PortraitName.FromFile(@"C:\pics\...png"));
    }

    [Fact]
    public void An_overlay_ending_is_refused_with_the_reason()
    {
        var check = PortraitName.Check("agnes-overlay");

        Assert.False(check.Ok);
        Assert.Contains("overlay", check.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_overlay_ending_is_stripped_when_it_comes_from_a_file_name()
    {
        Assert.Equal("agnes", PortraitName.FromFile(@"C:\pics\agnes-overlay.png"));
    }

    [Fact]
    public void Underscores_are_fine_because_nothing_is_parsed_out_of_the_name()
    {
        Assert.True(PortraitName.Check("special_n_custom1").Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has.dot")]
    public void Names_the_game_cannot_use_are_refused(string name)
    {
        Assert.False(PortraitName.Check(name).Ok);
    }

    [Fact]
    public void A_name_already_in_the_folder_is_never_written_over()
    {
        var taken = new HashSet<string> { "agnes" };

        Assert.Equal("agnes2", PortraitName.Available("agnes", taken.Contains));
    }

    [Fact]
    public void Numbering_keeps_climbing_while_names_are_taken()
    {
        var taken = new HashSet<string> { "agnes", "agnes2", "agnes3" };

        Assert.Equal("agnes4", PortraitName.Available("agnes", taken.Contains));
    }

    [Fact]
    public void A_free_name_is_left_alone()
    {
        Assert.Equal("agnes", PortraitName.Available("agnes", _ => false));
    }
}

/// <summary>
/// The shape a portrait is expected to be, measured from the game's own files rather
/// than assumed.
/// </summary>
public sealed class PortraitSizeTests
{
    [Fact]
    public void The_standard_size_is_the_one_the_game_uses_for_almost_all_of_them()
    {
        Assert.Equal(240, PortraitSize.Width);
        Assert.Equal(320, PortraitSize.Height);
        Assert.True(PortraitSize.IsStandard(240, 320));
    }

    [Fact]
    public void Nothing_is_said_about_an_image_that_is_already_the_right_size()
    {
        Assert.Null(PortraitSize.Advice(240, 320));
    }

    [Fact]
    public void An_image_of_the_right_shape_is_told_that_scaling_is_safe()
    {
        var advice = PortraitSize.Advice(480, 640);

        Assert.NotNull(advice);
        Assert.Contains("not distort", advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_image_of_the_wrong_shape_is_warned_it_will_be_squashed()
    {
        var advice = PortraitSize.Advice(500, 500);

        Assert.NotNull(advice);
        Assert.Contains("squash", advice, StringComparison.OrdinalIgnoreCase);
    }
}
