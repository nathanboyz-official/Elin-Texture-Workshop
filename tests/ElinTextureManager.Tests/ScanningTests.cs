using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

public class ScanningTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = true })
            .ScanSynchronously(ws.Paths);

    [Fact]
    public void FindsTexturesAndReadsPackageMetadata()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Anime NPC Pack",
            new[] { ("objC_2115.png", "a"), ("objC_1532.png", "b") }, author: "Nathan");

        var result = Scan(ws);
        var mod = Assert.Single(result.Mods);

        Assert.Equal("Anime NPC Pack", mod.Name);
        Assert.Equal("Nathan", mod.Author);
        Assert.Equal("111", mod.WorkshopId);
        Assert.Equal(2, mod.TextureCount);
        Assert.True(mod.HasTextureReplacements);
    }

    [Fact]
    public void BuildsIndexKeyedByTextureId()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_2115.png", "b") });
        ws.AddWorkshopMod("333", "Mod C", new[] { ("objC_9999.png", "c") });

        var result = Scan(ws);

        Assert.Equal(2, result.UniqueTextureCount);
        Assert.Equal(3, result.TextureFileCount);

        var entry = result.Index["objC_2115"];
        Assert.Equal(2, entry.Versions.Count);
        Assert.Equal(2, entry.SourceCount);
        Assert.Contains(entry.Versions, v => v.ModName == "Mod A");
        Assert.Contains(entry.Versions, v => v.ModName == "Mod B");
    }

    [Fact]
    public void DetectsConflictsOnlyWhenMoreThanOneModSuppliesATexture()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_100.png", "a"), ("objC_101.png", "a") });
        ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_100.png", "b") });

        var result = Scan(ws);

        Assert.Equal(1, result.ConflictCount);
        Assert.True(result.Index["objC_100"].HasConflict);
        Assert.False(result.Index["objC_101"].HasConflict);
    }

    [Fact]
    public void IdenticalFilesAreReportedAsOneUniqueImage()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "same-bytes") });
        ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_2115.png", "same-bytes") });

        var entry = Scan(ws).Index["objC_2115"];

        Assert.Equal(2, entry.SourceCount);
        Assert.Equal(1, entry.UniqueImageCount);
        Assert.True(entry.AllIdentical);
        Assert.Equal("2 sources, 1 unique", entry.VersionSummary);
    }

    [Fact]
    public void DifferentFilesAreReportedAsDistinctImages()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "one") });
        ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_2115.png", "two") });
        ws.AddWorkshopMod("333", "Mod C", new[] { ("objC_2115.png", "two") });

        var entry = Scan(ws).Index["objC_2115"];

        Assert.Equal(3, entry.SourceCount);
        Assert.Equal(2, entry.UniqueImageCount);
        Assert.False(entry.AllIdentical);
    }

    [Fact]
    public void SubFoldersOfTextureReplaceAreVariantsNotConflicts()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Outfit Pack", new[] { ("objC_2107.png", "default") });
        ws.AddVariant("111", "unused", "objC_2107.png", "alternate");

        var result = Scan(ws);
        var entry = result.Index["objC_2107"];

        Assert.Single(entry.Versions);
        Assert.Single(entry.Variants);
        Assert.False(entry.HasConflict);
        Assert.Equal(0, result.ConflictCount);
        Assert.Equal("unused", entry.Variants[0].VariantName);
        Assert.True(entry.Variants[0].IsVariant);
    }

    [Fact]
    public void ReadsPngPixelDimensionsFromTheHeader()
    {
        using var ws = new TestWorkspace();
        var mod = ws.AddWorkshopMod("111", "Mod A", Array.Empty<(string, string)>());
        TestWorkspace.WritePng(Path.Combine(mod, "Texture Replace", "objC_1.png"), "x", 128, 96);

        var texture = Assert.Single(Scan(ws).Mods[0].Textures);

        Assert.Equal(128, texture.PixelWidth);
        Assert.Equal(96, texture.PixelHeight);
        Assert.Equal("128 x 96", texture.DimensionsText);
    }

    [Fact]
    public void MissingPackageXmlIsFlaggedButTheModStillScans()
    {
        using var ws = new TestWorkspace();
        var dir = Path.Combine(ws.WorkshopRoot, "999", "Texture Replace");
        Directory.CreateDirectory(dir);
        TestWorkspace.WritePng(Path.Combine(dir, "objC_5.png"), "x");

        var mod = Assert.Single(Scan(ws).Mods);

        Assert.True(mod.MetadataMissing);
        Assert.Equal("999", mod.Name);   // falls back to the folder name
        Assert.Equal(1, mod.TextureCount);
    }

    [Fact]
    public void CorruptPackageXmlDoesNotStopTheScan()
    {
        using var ws = new TestWorkspace();
        var modDir = Path.Combine(ws.WorkshopRoot, "888");
        Directory.CreateDirectory(Path.Combine(modDir, "Texture Replace"));
        File.WriteAllText(Path.Combine(modDir, "package.xml"), "<Meta><title>broken");
        TestWorkspace.WritePng(Path.Combine(modDir, "Texture Replace", "objC_7.png"), "x");

        ws.AddWorkshopMod("111", "Healthy Mod", new[] { ("objC_8.png", "y") });

        var result = Scan(ws);

        Assert.Equal(2, result.ModCount);
        Assert.Equal(2, result.TextureFileCount);
        Assert.Contains(result.Mods, m => m.MetadataMissing);
    }

    [Fact]
    public void InvalidImageFilesAreLoggedAndSkippedNotFatal()
    {
        using var ws = new TestWorkspace();
        var mod = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_1.png", "good") });
        File.WriteAllText(Path.Combine(mod, "Texture Replace", "broken.png"), "not a png");

        var result = Scan(ws);

        Assert.Equal(2, result.TextureFileCount);
        Assert.Contains(result.Errors, e => e.Contains("Unreadable or invalid PNG"));
        Assert.Equal(0, result.Index["broken"].Versions[0].PixelWidth);
    }

    [Fact]
    public void NonImageFilesAreIgnored()
    {
        using var ws = new TestWorkspace();
        var mod = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_1.png", "good") });
        File.WriteAllText(Path.Combine(mod, "Texture Replace", "readme.txt"), "hello");
        File.WriteAllText(Path.Combine(mod, "Texture Replace", "source.clip"), "binary");

        Assert.Equal(1, Scan(ws).TextureFileCount);
    }

    [Fact]
    public void ModsWithoutTextureReplaceAreStillListed()
    {
        using var ws = new TestWorkspace();
        var dir = Path.Combine(ws.WorkshopRoot, "555");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.xml"),
            "<?xml version=\"1.0\"?><Meta><title>Code Mod</title></Meta>");

        var result = Scan(ws);
        var mod = Assert.Single(result.Mods);

        Assert.Equal("Code Mod", mod.Name);
        Assert.False(mod.HasTextureReplacements);
        Assert.Equal(0, result.TextureModCount);
    }

    [Fact]
    public void PrefixHistogramSurfacesUnknownPrefixes()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A",
            new[] { ("objC_1.png", "a"), ("objC_2.png", "b"), ("weird_thing.png", "c") });

        var histogram = TextureIndexBuilder.PrefixHistogram(Scan(ws));

        Assert.Contains(histogram, h => h.Prefix == "objC" && h.Count == 2);
        Assert.Contains(histogram, h => h.Prefix == "weird_thing");
    }
}
