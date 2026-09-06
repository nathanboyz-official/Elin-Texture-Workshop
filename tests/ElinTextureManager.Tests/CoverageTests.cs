using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The replacement folders beyond "Texture Replace" and "Portrait". A mod that only
/// ships PCC parts or loose Texture files used to be completely invisible, which on a
/// real library is more images than the ones that did show up.
/// </summary>
public sealed class CoverageTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = false }).ScanSynchronously(ws.Paths);

    private static void AddFile(TestWorkspace ws, string modId, string relative, string content)
    {
        var path = Path.Combine(ws.WorkshopRoot, modId, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestWorkspace.WritePng(path, content);
    }

    [Fact]
    public void Pcc_parts_are_indexed_and_grouped_by_layer()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        AddFile(ws, "100", @"Actor\PCC\female\pcc_hair_x.png", "hair");
        AddFile(ws, "100", @"Actor\PCC\female\pcc_cloth_y.png", "cloth");

        var scan = Scan(ws);
        var pcc = scan.Index.Values.Where(e => e.Kind == ReplacementKind.Pcc).ToList();

        Assert.Equal(2, pcc.Count);
        Assert.All(pcc, e => Assert.Equal(TextureCategory.Pcc, e.Category));
        Assert.Contains(pcc, e => e.Prefix == PccPart.Hair);
        Assert.Contains(pcc, e => e.Prefix == PccPart.Cloth);
    }

    [Fact]
    public void The_same_part_name_under_female_and_male_stays_two_entries()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        AddFile(ws, "100", @"Actor\PCC\female\pcc_hair_x.png", "she");
        AddFile(ws, "100", @"Actor\PCC\male\pcc_hair_x.png", "he");

        var scan = Scan(ws);

        // Different images; collapsing them would send an override to the wrong one.
        Assert.Equal(2, scan.Index.Count);
        Assert.All(scan.Index.Values, e => Assert.False(e.HasConflict));
    }

    [Fact]
    public void Loose_Texture_and_TextureforTE_folders_are_indexed()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        AddFile(ws, "100", @"Texture\hoshino.png", "sprite");
        AddFile(ws, "100", @"TextureforTE\objC_600#hostility-enemy.png", "variant");

        var scan = Scan(ws);

        Assert.Contains(scan.Index.Values, e => e.Kind == ReplacementKind.Texture);
        var te = Assert.Single(scan.Index.Values, e => e.Kind == ReplacementKind.TextureExpand);
        Assert.Equal("hostility-enemy", te.Prefix);      // grouped by the condition
    }

    [Fact]
    public void A_name_shared_across_folders_does_not_merge_into_one_entry()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("shared.png", "sprite") });
        AddFile(ws, "100", @"Texture\shared.png", "loose");
        AddFile(ws, "100", @"Portrait\shared.png", "portrait");

        var scan = Scan(ws);

        Assert.Equal(3, scan.Index.Count);
        Assert.All(scan.Index.Values, e => Assert.False(e.HasConflict));
        Assert.All(scan.Index.Values, e => Assert.Equal("shared", e.DisplayId));
    }

    [Fact]
    public void The_search_for_one_folder_never_descends_into_another()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());

        // A "Texture" folder nested inside "Texture Replace" must not be indexed twice.
        AddFile(ws, "100", @"Texture Replace\Texture\objC_9.png", "nested");

        var scan = Scan(ws);

        Assert.DoesNotContain(scan.Index.Values, e => e.Kind == ReplacementKind.Texture);
    }

    [Fact]
    public void An_override_for_each_kind_lands_in_the_matching_package_folder()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", Array.Empty<(string, string)>());
        AddFile(ws, "100", @"Actor\PCC\female\pcc_hair_x.png", "hair");

        var scan = Scan(ws);
        var selections = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, selections);

        var part = Assert.Single(scan.Index.Values);
        Assert.True(overrides.Select(part.Versions[0]).Success);

        var expected = Path.Combine(ws.Paths.OverridePackageRoot, "Actor", "PCC", "pcc_hair_x.png");
        Assert.True(File.Exists(expected), $"expected the copy at {expected}");
    }
}
