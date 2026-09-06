using ElinTextureManager.Core.Model;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The limits the game puts on package.xml, checked against what its own code does.
///
/// BaseModPackage reads loadPriority with int.TryParse and then Mathf.Clamp(result,
/// -999, 999). Both halves of that fail quietly: a number too large is silently moved,
/// and a value that is not a number at all leaves the priority untouched at the default.
/// </summary>
public sealed class PackageLimitsTests
{
    [Theory]
    [InlineData("100", 100)]
    [InlineData("0", 0)]
    [InlineData("-500", -500)]
    [InlineData("999", 999)]
    [InlineData("-999", -999)]
    public void A_priority_inside_the_range_is_used_as_written(string declared, int expected)
    {
        Assert.Equal(expected, PackageLimits.Effective(declared));
    }

    [Theory]
    [InlineData("1000", 999)]
    [InlineData("5181", 999)]
    [InlineData("9000", 999)]
    [InlineData("114514", 999)]
    [InlineData("-1000", -999)]
    public void A_priority_outside_the_range_is_clamped_the_way_the_game_clamps_it(
        string declared, int expected)
    {
        Assert.Equal(expected, PackageLimits.Effective(declared));
    }

    [Theory]
    [InlineData("last")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("99.5")]
    [InlineData(null)]
    public void Anything_that_is_not_a_whole_number_falls_back_to_the_default(string? declared)
    {
        Assert.Equal(PackageLimits.DefaultLoadPriority, PackageLimits.Effective(declared));
    }

    [Fact]
    public void The_bounds_are_the_ones_the_game_declares()
    {
        Assert.Equal(100, PackageLimits.DefaultLoadPriority);
        Assert.Equal(-999, PackageLimits.MinLoadPriority);
        Assert.Equal(999, PackageLimits.MaxLoadPriority);
    }

    [Fact]
    public void A_mod_knows_when_the_game_moved_it()
    {
        var mod = new ModPackage
        {
            Key = "x",
            Directory = "d",
            Name = "Dungeon Maker",
            DeclaredLoadPriority = "114514",
            LoadPriority = PackageLimits.Effective("114514"),
        };

        Assert.True(mod.LoadPriorityWasClamped);
        Assert.False(mod.LoadPriorityUnreadable);
        Assert.Equal(999, mod.LoadPriority);
    }

    [Fact]
    public void A_mod_knows_when_the_game_could_not_read_it()
    {
        var mod = new ModPackage
        {
            Key = "x",
            Directory = "d",
            Name = "Odd One",
            DeclaredLoadPriority = "last",
            LoadPriority = PackageLimits.Effective("last"),
        };

        Assert.True(mod.LoadPriorityUnreadable);
        Assert.False(mod.LoadPriorityWasClamped);
        Assert.Equal(100, mod.LoadPriority);
    }

    [Fact]
    public void An_ordinary_priority_raises_nothing()
    {
        var mod = new ModPackage
        {
            Key = "x",
            Directory = "d",
            Name = "Normal",
            DeclaredLoadPriority = "100",
            LoadPriority = 100,
        };

        Assert.False(mod.LoadPriorityWasClamped);
        Assert.False(mod.LoadPriorityUnreadable);
    }
}
