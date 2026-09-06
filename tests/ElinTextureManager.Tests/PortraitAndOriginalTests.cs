using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Portrait replacements, and the base game's own files shown as the original.
/// </summary>
public sealed class PortraitAndOriginalTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = true }).ScanSynchronously(ws.Paths);

    [Fact]
    public void A_mods_Portrait_folder_is_indexed()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Portraits", new[] { ("objC_1.png", "a") });
        ws.AddPortraits("100", ("UN_ashland.png", "p"));

        var scan = Scan(ws);
        var mod = Assert.Single(scan.Mods, m => m.Key == "100");

        Assert.Equal(1, mod.PortraitCount);
        Assert.Equal(1, mod.SpriteCount);
    }

    [Fact]
    public void A_portrait_and_a_sprite_of_the_same_name_stay_separate()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Both", new[] { ("shared.png", "sprite") });
        ws.AddPortraits("100", ("shared.png", "portrait"));

        var scan = Scan(ws);

        // Two entries, not one merged entry that would send an override to the wrong folder.
        Assert.Equal(2, scan.Index.Count);
        Assert.Contains(scan.Index.Values, e => e.Kind == ReplacementKind.TextureReplace);
        Assert.Contains(scan.Index.Values, e => e.Kind == ReplacementKind.Portrait);

        // Both read as the same plain name.
        Assert.All(scan.Index.Values, e => Assert.Equal("shared", e.DisplayId));
    }

    [Fact]
    public void Two_mods_replacing_the_same_portrait_conflict()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddWorkshopMod("200", "B", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "one"));
        ws.AddPortraits("200", ("UN_ashland.png", "two"));

        var scan = Scan(ws);
        var entry = Assert.Single(scan.Index.Values);

        Assert.True(entry.HasConflict);
        Assert.Equal(2, entry.SourceCount);
    }

    [Fact]
    public void A_portrait_override_is_written_into_the_packages_Portrait_folder()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "one"));

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        var source = Assert.Single(scan.Index.Values).Versions[0];
        Assert.True(overrides.Select(source).Success);

        Assert.True(File.Exists(Path.Combine(ws.Paths.OverridePortraitRoot, "UN_ashland.png")));
        Assert.False(File.Exists(Path.Combine(ws.Paths.OverrideTextureRoot, "UN_ashland.png")));
    }

    [Fact]
    public void Removing_a_portrait_override_deletes_only_that_copy()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "one"));

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        var entry = Assert.Single(scan.Index.Values);
        overrides.Select(entry.Versions[0]);

        Assert.True(overrides.Remove(entry.TextureId).Success);

        Assert.False(File.Exists(Path.Combine(ws.Paths.OverridePortraitRoot, "UN_ashland.png")));
        Assert.True(File.Exists(entry.Versions[0].FullPath));
    }

    // ---- the base game's own files ----

    [Fact]
    public void The_original_is_found_for_a_replaced_portrait()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "modded"));

        var scan = Scan(ws);
        VanillaAssets.Load(ws.Paths).AttachTo(scan);

        var entry = Assert.Single(scan.Index.Values);

        Assert.True(entry.HasVanilla);
        Assert.Equal(TextureSourceType.Vanilla, entry.Vanilla!.SourceType);
        Assert.Equal(ReplacementKind.Portrait, entry.Vanilla.Kind);
    }

    [Fact]
    public void A_sprite_with_no_loose_original_reports_none()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", new[] { ("objC_2115.png", "modded") });

        var scan = Scan(ws);
        VanillaAssets.Load(ws.Paths).AttachTo(scan);

        // objC_* addresses a slot in a packed atlas, so there is nothing loose to show.
        Assert.False(scan.Index["objC_2115"].HasVanilla);
    }

    [Fact]
    public void The_base_game_package_is_not_indexed_as_a_mod()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "modded"));

        var scan = Scan(ws);
        var entry = Assert.Single(scan.Index.Values);

        // Otherwise every replaced portrait would look like a two-mod conflict.
        Assert.False(entry.HasConflict);
        Assert.Single(entry.Versions);

        var elona = Assert.Single(scan.Mods, m => m.Key == "_Elona");
        Assert.Equal(TextureSourceType.Vanilla, elona.SourceType);
        Assert.Empty(elona.Textures);
    }

    [Fact]
    public void A_mod_shipping_the_untouched_original_is_detectable()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "same");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "same"));

        var scan = Scan(ws);
        VanillaAssets.Load(ws.Paths).AttachTo(scan);

        var entry = Assert.Single(scan.Index.Values);

        Assert.Equal(entry.Vanilla!.Hash, entry.Versions[0].Hash);
    }

    [Fact]
    public void Selecting_the_original_puts_it_back_without_touching_the_base_game()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "modded"));

        var scan = Scan(ws);
        var vanilla = VanillaAssets.Load(ws.Paths);
        vanilla.AttachTo(scan);

        var entry = Assert.Single(scan.Index.Values);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        Assert.True(overrides.Select(entry.Vanilla!).Success);

        var copy = Path.Combine(ws.Paths.OverridePortraitRoot, "UN_ashland.png");
        Assert.True(File.Exists(copy));
        Assert.Equal(File.ReadAllBytes(entry.Vanilla!.FullPath), File.ReadAllBytes(copy));

        // The source under Package\_Elona is left exactly as it was.
        Assert.True(File.Exists(entry.Vanilla.FullPath));
    }
}
