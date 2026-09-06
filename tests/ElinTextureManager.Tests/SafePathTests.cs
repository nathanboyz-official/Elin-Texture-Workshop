using ElinTextureManager.Core.Overrides;
using Xunit;

namespace ElinTextureManager.Tests;

public class SafePathTests
{
    [Fact]
    public void AllowsDeletingAFileInsideTheOverrideFolder()
    {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.Paths.OverrideTextureRoot);

        var file = Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png");
        TestWorkspace.WritePng(file, "x");

        var verdict = SafePath.CanDeleteOverrideFile(file, ws.Paths, "objC_2115.png");

        Assert.True(verdict.Allowed);
    }

    [Fact]
    public void RefusesToDeleteInsideTheWorkshopFolder()
    {
        using var ws = new TestWorkspace();
        var mod = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var workshopFile = Path.Combine(mod, "Texture Replace", "objC_2115.png");

        var verdict = SafePath.CanDeleteOverrideFile(workshopFile, ws.Paths, "objC_2115.png");

        Assert.False(verdict.Allowed);
        Assert.True(File.Exists(workshopFile));
    }

    [Fact]
    public void RefusesToDeleteInsideTheBaseGameAssetFolder()
    {
        using var ws = new TestWorkspace();
        var asset = Path.Combine(ws.ElinRoot, "Elin_Data", "resources.assets");
        File.WriteAllText(asset, "game data");

        var verdict = SafePath.CanDeleteOverrideFile(asset, ws.Paths);

        Assert.False(verdict.Allowed);
        Assert.True(File.Exists(asset));
    }

    [Fact]
    public void RefusesTraversalOutOfTheOverrideFolder()
    {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.Paths.OverrideTextureRoot);

        var outside = Path.Combine(ws.ElinRoot, "loadorder.txt");
        File.WriteAllText(outside, "data");

        var escape = Path.Combine(ws.Paths.OverrideTextureRoot, "..", "..", "..", "loadorder.txt");
        var verdict = SafePath.CanDeleteOverrideFile(escape, ws.Paths);

        Assert.False(verdict.Allowed);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void RefusesWhenTheFileNameDoesNotMatchTheSelection()
    {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.Paths.OverrideTextureRoot);

        var file = Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png");
        TestWorkspace.WritePng(file, "x");

        var verdict = SafePath.CanDeleteOverrideFile(file, ws.Paths, "objC_9999.png");

        Assert.False(verdict.Allowed);
        Assert.Contains("does not match", verdict.Reason);
    }

    [Fact]
    public void RefusesToDeleteADirectory()
    {
        using var ws = new TestWorkspace();
        Directory.CreateDirectory(ws.Paths.OverrideTextureRoot);

        var verdict = SafePath.CanDeleteOverrideFile(ws.Paths.OverrideTextureRoot, ws.Paths);

        Assert.False(verdict.Allowed);
        Assert.True(Directory.Exists(ws.Paths.OverrideTextureRoot));
    }

    [Fact]
    public void RefusesMissingFiles()
    {
        using var ws = new TestWorkspace();
        var missing = Path.Combine(ws.Paths.OverrideTextureRoot, "nope.png");

        Assert.False(SafePath.CanDeleteOverrideFile(missing, ws.Paths).Allowed);
    }

    [Fact]
    public void RefusesWritesOutsideTheOverridePackage()
    {
        using var ws = new TestWorkspace();

        Assert.False(SafePath.CanWriteOverrideFile(
            Path.Combine(ws.WorkshopRoot, "111", "Texture Replace", "objC_1.png"), ws.Paths).Allowed);

        Assert.False(SafePath.CanWriteOverrideFile(
            Path.Combine(ws.ElinRoot, "Elin_Data", "x.png"), ws.Paths).Allowed);

        Assert.True(SafePath.CanWriteOverrideFile(
            Path.Combine(ws.Paths.OverrideTextureRoot, "objC_1.png"), ws.Paths).Allowed);
    }

    [Theory]
    [InlineData("objC_2115.png", true)]
    [InlineData("obj_1.png", true)]
    [InlineData("../escape.png", false)]
    [InlineData("..\\escape.png", false)]
    [InlineData("sub/objC_1.png", false)]
    [InlineData("", false)]
    [InlineData("..", false)]
    public void ValidatesFileNames(string name, bool expected)
    {
        Assert.Equal(expected, SafePath.IsSafeFileName(name));
    }

    [Fact]
    public void IsInsideTreatsTheRootItselfAsInside()
    {
        Assert.True(SafePath.IsInside(@"C:\a\b", @"C:\a\b"));
        Assert.True(SafePath.IsInside(@"C:\a\b\c.png", @"C:\a\b"));
        Assert.False(SafePath.IsInside(@"C:\a\bc\d.png", @"C:\a\b"));
        Assert.False(SafePath.IsInside(@"C:\a", @"C:\a\b"));
    }

    [Fact]
    public void IsInsideIsCaseInsensitiveOnWindowsPaths()
    {
        Assert.True(SafePath.IsInside(@"C:\Games\Elin\Package\x.png", @"c:\games\elin\package"));
    }
}
