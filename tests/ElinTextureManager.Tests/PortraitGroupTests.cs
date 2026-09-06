using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// How portrait names are grouped, and how the "-overlay" layer is folded into the
/// portrait it belongs to. Both are driven purely by the file name, which is all Elin
/// itself has to go on.
/// </summary>
public sealed class PortraitGroupTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = false }).ScanSynchronously(ws.Paths);

    [Theory]
    [InlineData("c_f-1", PortraitGroup.Female)]
    [InlineData("c_m-12", PortraitGroup.Male)]
    [InlineData("guard_f-2", PortraitGroup.Female)]
    [InlineData("foxfolk_m-1", PortraitGroup.Male)]
    [InlineData("special_f_younglady", PortraitGroup.Female)]
    [InlineData("special_n-yeek", PortraitGroup.Neutral)]
    [InlineData("special_n_slime_pink", PortraitGroup.Neutral)]
    public void The_gender_segment_decides_the_group(string name, string expected)
    {
        Assert.Equal(expected, PortraitGroup.ForName(name));
    }

    [Theory]
    [InlineData("BG_3")]
    [InlineData("BGF_1")]
    [InlineData("BG_bb1")]
    public void BG_names_are_backgrounds_not_characters(string name)
    {
        Assert.Equal(PortraitGroup.Background, PortraitGroup.ForName(name));
    }

    [Fact]
    public void A_name_merely_starting_with_BG_is_not_a_background()
    {
        // "BG" has to be a whole prefix; a character called BGirl is a character.
        Assert.NotEqual(PortraitGroup.Background, PortraitGroup.ForName("BGirl_f-1"));
    }

    [Fact]
    public void Named_characters_are_their_own_group()
    {
        Assert.Equal(PortraitGroup.Named, PortraitGroup.ForName("UN_ashland"));
    }

    [Fact]
    public void An_upper_case_letter_inside_a_word_is_not_a_gender_marker()
    {
        // Real mod naming: UN_azurlane_IJN_* must not read as neutral.
        Assert.Equal(PortraitGroup.Named, PortraitGroup.ForName("UN_azurlane_IJN_Ayanami"));
    }

    [Fact]
    public void A_gender_marker_wins_over_the_named_prefix()
    {
        Assert.Equal(PortraitGroup.Female, PortraitGroup.ForName("UN_someone_f-1"));
    }

    [Fact]
    public void An_unrecognisable_name_is_grouped_rather_than_dropped()
    {
        Assert.Equal(PortraitGroup.Other, PortraitGroup.ForName("Kubrika"));
    }

    [Fact]
    public void An_overlay_is_grouped_with_the_portrait_it_belongs_to()
    {
        Assert.Equal(
            PortraitGroup.ForName("c_f-1"),
            PortraitGroup.ForName("c_f-1-overlay"));
    }

    // ---- folding an overlay into its portrait ----

    [Fact]
    public void An_overlay_is_hung_off_the_portrait_it_belongs_to()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("c_f-1.png", "base"), ("c_f-1-overlay.png", "layer"));

        var scan = Scan(ws);
        var portrait = scan.Index[TextureIdentity.PortraitNamespace + "c_f-1"];
        var overlay = scan.Index[TextureIdentity.PortraitNamespace + "c_f-1-overlay"];

        Assert.True(portrait.HasOverlay);
        Assert.Same(overlay, portrait.Overlay);

        // The overlay knows it is spoken for, which is what keeps it out of the grid.
        Assert.True(overlay.IsAttachedOverlay);
        Assert.False(portrait.IsAttachedOverlay);
    }

    [Fact]
    public void An_overlay_with_no_portrait_stays_reachable()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("c_f-9-overlay.png", "layer"));

        var scan = Scan(ws);
        var overlay = Assert.Single(scan.Index.Values);

        // Nothing to fold it into, so hiding it would make it unreachable.
        Assert.True(overlay.IsOverlay);
        Assert.False(overlay.IsAttachedOverlay);
    }

    [Fact]
    public void An_overlay_keeps_its_own_versions_and_conflicts()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddWorkshopMod("200", "B", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("c_f-1.png", "base"), ("c_f-1-overlay.png", "one"));
        ws.AddPortraits("200", ("c_f-1-overlay.png", "two"));

        var scan = Scan(ws);
        var portrait = scan.Index[TextureIdentity.PortraitNamespace + "c_f-1"];

        // Folding it in is a display decision; it is still its own overridable file.
        Assert.False(portrait.HasConflict);
        Assert.True(portrait.Overlay!.HasConflict);
        Assert.Equal(2, portrait.Overlay.SourceCount);
    }

    [Fact]
    public void A_portrait_is_categorised_by_its_folder_not_its_group()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("BG_3.png", "bg"), ("c_m-1.png", "male"));

        var scan = Scan(ws);

        // The prefix carries the group, so the category must not be derived from it.
        Assert.All(scan.Index.Values, e => Assert.Equal(TextureCategory.Portraits, e.Category));

        Assert.Equal(
            PortraitGroup.Background,
            scan.Index[TextureIdentity.PortraitNamespace + "BG_3"].Prefix);
        Assert.Equal(
            PortraitGroup.Male,
            scan.Index[TextureIdentity.PortraitNamespace + "c_m-1"].Prefix);
    }

    [Fact]
    public void A_conflict_on_an_overlay_is_reported_against_its_portrait()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddWorkshopMod("200", "B", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("c_f-1.png", "base"), ("c_f-1-overlay.png", "one"));
        ws.AddPortraits("200", ("c_f-1-overlay.png", "two"));

        var scan = Scan(ws);
        var portrait = scan.Index[TextureIdentity.PortraitNamespace + "c_f-1"];

        // The overlay has no tile, so the conflict has to surface on something that does.
        var conflict = Assert.Single(scan.Conflicts);
        Assert.Same(portrait, conflict);
        Assert.Equal(1, scan.ConflictCount);
    }

    [Fact]
    public void An_overlay_conflict_is_counted_once_even_when_the_portrait_conflicts_too()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddWorkshopMod("200", "B", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("c_f-1.png", "one"), ("c_f-1-overlay.png", "one"));
        ws.AddPortraits("200", ("c_f-1.png", "two"), ("c_f-1-overlay.png", "two"));

        var scan = Scan(ws);

        Assert.Equal(1, scan.ConflictCount);
    }

    [Fact]
    public void Sprite_prefixes_are_untouched_by_portrait_grouping()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("objC_2115.png", "a") });

        var entry = Scan(ws).Index["objC_2115"];

        Assert.Equal("objC", entry.Prefix);
        Assert.Equal(TextureCategory.Characters, entry.Category);
    }
}
