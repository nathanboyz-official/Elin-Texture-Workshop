using ElinTextureManager.Core.Model;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Reading TextureExpand file names.
///
/// One sprite becomes up to eight files, because the conditions stack - drunk, asleep,
/// furred, and every combination. Every name below is one taken from a real library.
/// </summary>
public sealed class TextureExpandNameTests
{
    [Fact]
    public void A_plain_sprite_has_no_conditions()
    {
        var parts = TextureExpandName.Parse("objC_1303.png");

        Assert.Equal("objC_1303", parts.Sprite);
        Assert.Empty(parts.Conditions);
        Assert.True(parts.IsBase);
        Assert.Equal("normally", parts.ConditionText);
    }

    [Fact]
    public void One_condition_is_read_off_the_name()
    {
        var parts = TextureExpandName.Parse("objC_1303#con-drunk.png");

        Assert.Equal("objC_1303", parts.Sprite);
        Assert.Equal(new[] { "con-drunk" }, parts.Conditions);
        Assert.False(parts.IsBase);
    }

    [Fact]
    public void Conditions_stack_and_all_of_them_are_kept()
    {
        var parts = TextureExpandName.Parse("objC_1303#fur#con-sleep#con-drunk.png");

        Assert.Equal("objC_1303", parts.Sprite);
        Assert.Equal(new[] { "fur", "con-sleep", "con-drunk" }, parts.Conditions);
        Assert.Equal("fur + con-sleep + con-drunk", parts.ConditionText);
    }

    [Fact]
    public void Every_condition_file_of_a_sprite_reports_the_same_sprite()
    {
        // The whole point: eight files, one decision.
        var names = new[]
        {
            "objC_1303.png",
            "objC_1303#con-drunk.png",
            "objC_1303#con-sleep.png",
            "objC_1303#con-sleep#con-drunk.png",
            "objC_1303#fur.png",
            "objC_1303#fur#con-drunk.png",
            "objC_1303#fur#con-sleep.png",
            "objC_1303#fur#con-sleep#con-drunk.png",
        };

        Assert.Single(names.Select(TextureExpandName.SpriteOf).Distinct());
        Assert.Equal("objC_1303", TextureExpandName.SpriteOf(names[7]));
    }

    [Fact]
    public void The_folder_a_mod_keeps_it_in_is_not_part_of_the_sprite()
    {
        // Two mods put the same sprite under folders of their own naming.
        Assert.Equal("objCL_27",
            TextureExpandName.SpriteOf("TextureforTE/objCL_27#hostility-enemy.png"));

        Assert.Equal("objCL_27",
            TextureExpandName.SpriteOf(@"Texture\objCL_27#hostility-enemy.png"));
    }

    [Theory]
    [InlineData("objC_1916#f.png", "objC_1916", "f")]
    [InlineData("objC_2115#month-winter.png", "objC_2115", "month-winter")]
    [InlineData("objC_1016#Con-Sleep.png", "objC_1016", "Con-Sleep")]
    public void The_conditions_a_real_library_uses_all_read_correctly(
        string file, string sprite, string condition)
    {
        var parts = TextureExpandName.Parse(file);

        Assert.Equal(sprite, parts.Sprite);
        Assert.Equal(condition, Assert.Single(parts.Conditions));
    }

    [Fact]
    public void A_name_that_is_nothing_but_hashes_does_not_throw()
    {
        Assert.Equal(string.Empty, TextureExpandName.Parse("###.png").Sprite);
    }
}
