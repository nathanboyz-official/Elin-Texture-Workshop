using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// What counts as a conflict, and what does not.
///
/// A conflict is two or more MODS wanting the same slot. The base game's own file is
/// what a mod replaces rather than a rival to it, and the copy this application writes
/// when you choose a winner is not a competing mod either - counting that would mean
/// resolving a conflict left it still reported as one.
/// </summary>
public sealed class ConflictDefinitionTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = true }).ScanSynchronously(ws.Paths);

    [Fact]
    public void One_mod_replacing_a_vanilla_image_is_not_a_conflict()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "modded"));

        var scan = Scan(ws);
        VanillaAssets.Load(ws.Paths).AttachTo(scan);

        var entry = Assert.Single(scan.Index.Values);

        // The original is available to switch back to, but it is not a rival source.
        Assert.True(entry.HasVanilla);
        Assert.False(entry.HasConflict);
        Assert.Equal(1, entry.SourceCount);
        Assert.Equal(0, scan.ConflictCount);
    }

    [Fact]
    public void Two_mods_replacing_the_same_image_is_a_conflict()
    {
        using var ws = new TestWorkspace();
        ws.AddVanillaImage(Path.Combine("Portrait", "UN_ashland.png"), "vanilla");
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        ws.AddWorkshopMod("200", "B", Array.Empty<(string, string)>());
        ws.AddPortraits("100", ("UN_ashland.png", "one"));
        ws.AddPortraits("200", ("UN_ashland.png", "two"));

        var scan = Scan(ws);
        VanillaAssets.Load(ws.Paths).AttachTo(scan);

        var entry = Assert.Single(scan.Index.Values);

        Assert.True(entry.HasConflict);
        Assert.Equal(2, entry.SourceCount);
        Assert.Equal(1, scan.ConflictCount);
    }

    [Fact]
    public void Choosing_a_winner_does_not_turn_one_mod_into_a_conflict()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("objC_2115.png", "only") });

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        // Rescan: the override package is now on disk and gets indexed like any other.
        var rescan = Scan(ws);
        var entry = rescan.Index["objC_2115"];

        Assert.Equal(2, entry.Versions.Count);   // the mod's file and our copy of it
        Assert.Equal(1, entry.SourceCount);      // but only one mod supplies it
        Assert.False(entry.HasConflict);
        Assert.Equal(0, rescan.ConflictCount);
    }

    [Fact]
    public void An_override_copy_does_not_inflate_a_real_conflicts_source_count()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("objC_2115.png", "one") });
        ws.AddWorkshopMod("200", "B", new[] { ("objC_2115.png", "two") });

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        var entry = Scan(ws).Index["objC_2115"];

        Assert.Equal(3, entry.Versions.Count);
        Assert.Equal(2, entry.SourceCount);
        Assert.True(entry.HasConflict);
    }

    [Fact]
    public void Identical_only_compares_what_the_mods_ship()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("objC_2115.png", "same") });
        ws.AddWorkshopMod("200", "B", new[] { ("objC_2115.png", "same") });

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        new OverrideManager(ws.Paths, selections).Select(scan.Index["objC_2115"].Versions[0]);

        var entry = Scan(ws).Index["objC_2115"];

        // Our copy is a third identical file; it must not change the verdict either way.
        Assert.True(entry.AllIdentical);
        Assert.Equal(1, entry.UniqueImageCount);
    }
}
