using ElinTextureManager.Core.Model;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// How images are grouped for the browser's filter.
///
/// The raw prefix is a poor grouping on its own: a name with no numeric tail keeps the
/// whole name as its prefix, so every one of them became a group of one. On the real
/// library that meant 408 groups whose names all started with "azurlane" and 1,472
/// groups in total, which is a dropdown that groups nothing.
/// </summary>
public sealed class GroupingTests
{
    private static TextureEntry Entry(string prefix, string id) => new()
    {
        TextureId = id,
        Prefix = prefix,
    };

    /// <summary>Several files from one mod, each of which would be its own prefix.</summary>
    private static IEnumerable<TextureEntry> Family(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => Entry($"{prefix}_thing{i}", $"{prefix}_thing{i}"));

    [Fact]
    public void Names_sharing_a_leading_word_are_one_group()
    {
        var index = TextureGrouping.Build(Family("azurlane", 5).ToList());

        var group = Assert.Single(index.Groups);
        Assert.Equal("azurlane", group.Name);
        Assert.Equal(5, group.Count);
    }

    [Fact]
    public void A_group_of_one_is_not_a_group()
    {
        var entries = Family("azurlane", 4).Append(Entry("loner", "loner")).ToList();

        var index = TextureGrouping.Build(entries);

        Assert.Equal(2, index.Count);
        Assert.Contains(index.Groups, g => g.Name == "azurlane" && g.Count == 4);
        Assert.Contains(index.Groups, g => g.Name == TextureGrouping.Other && g.Count == 1);
    }

    [Fact]
    public void Every_leftover_lands_in_one_Other_rather_than_a_line_each()
    {
        var entries = new[] { "alpha", "beta", "gamma", "delta" }.Select(n => Entry(n, n)).ToList();

        var index = TextureGrouping.Build(entries);

        var only = Assert.Single(index.Groups);
        Assert.Equal(TextureGrouping.Other, only.Name);
        Assert.Equal(4, only.Count);
    }

    [Fact]
    public void Other_is_listed_last_however_big_it_gets()
    {
        var entries = Family("azurlane", 3)
            .Concat(Enumerable.Range(0, 40).Select(i => Entry($"one{i}", $"one{i}")))
            .ToList();

        var index = TextureGrouping.Build(entries);

        // Otherwise the biggest bucket - the one that means "no family" - sits at the
        // top of the list and reads as the most important group.
        Assert.Equal(TextureGrouping.Other, index.Groups[^1].Name);
        Assert.Equal("azurlane", index.Groups[0].Name);
    }

    [Fact]
    public void Groups_that_were_already_good_are_left_alone()
    {
        // Characters, portraits and PCC parts already group well - objC, Named, Cloth.
        // Measured on the real library these went 3 to 3, 6 to 6 and 7 to 7.
        var entries = Enumerable.Repeat("objC", 30).Select((p, i) => Entry(p, $"objC_{i}"))
            .Concat(Enumerable.Repeat("objCL", 4).Select((p, i) => Entry(p, $"objCL_{i}")))
            .ToList();

        var index = TextureGrouping.Build(entries);

        Assert.Equal(2, index.Count);
        Assert.Contains(index.Groups, g => g.Name == "objC" && g.Count == 30);
        Assert.Contains(index.Groups, g => g.Name == "objCL" && g.Count == 4);
    }

    [Fact]
    public void A_hyphen_does_not_split_a_family()
    {
        // "con-sleep" is one condition name in Elin's own convention. Splitting there
        // would invent a "con" group that nobody named.
        Assert.Equal("con-sleep", TextureGrouping.FamilyOf("con-sleep"));
        Assert.Equal("special", TextureGrouping.FamilyOf("special_n-bb18"));
        Assert.Equal("fur", TextureGrouping.FamilyOf("fur#con-sleep"));
        Assert.Equal("objC", TextureGrouping.FamilyOf("objC_1407#con-sleep"));
    }

    [Fact]
    public void An_entry_reports_the_group_it_is_actually_shown_under()
    {
        var entries = Family("azurlane", 3).Append(Entry("loner", "loner")).ToList();
        var index = TextureGrouping.Build(entries);

        Assert.Equal("azurlane", index.GroupOf(entries[0]));

        // The lone one is filed under Other, so filtering by Other has to find it.
        Assert.Equal(TextureGrouping.Other, index.GroupOf(entries[^1]));
    }

    [Fact]
    public void An_empty_or_missing_prefix_is_not_its_own_group()
    {
        Assert.Equal(TextureGrouping.Other, TextureGrouping.FamilyOf(null));
        Assert.Equal(TextureGrouping.Other, TextureGrouping.FamilyOf(""));
        Assert.Equal(TextureGrouping.Other, TextureGrouping.FamilyOf("   "));
        Assert.Equal(TextureGrouping.Other, TextureGrouping.FamilyOf("_leading"));
    }

    [Fact]
    public void Nothing_at_all_produces_no_groups_rather_than_an_empty_Other()
    {
        Assert.Empty(TextureGrouping.Build(Array.Empty<TextureEntry>()).Groups);
    }
}
