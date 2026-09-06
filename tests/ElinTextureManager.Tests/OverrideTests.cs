using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

public class OverrideTests
{
    private static (ScanResult scan, OverrideManager overrides, SelectionStore store)
        Setup(TestWorkspace ws)
    {
        var store = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, store);
        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        return (scan, overrides, store);
    }

    [Fact]
    public void CreatesTheOverridePackageWithValidMetadata()
    {
        using var ws = new TestWorkspace();
        var (_, overrides, _) = Setup(ws);

        Assert.True(overrides.EnsurePackage().Success);
        Assert.True(Directory.Exists(ws.Paths.OverrideTextureRoot));
        Assert.True(File.Exists(ws.Paths.OverridePackageXml));

        var xml = File.ReadAllText(ws.Paths.OverridePackageXml);
        Assert.Contains("<Meta>", xml);
        // Exactly the game's maximum, not one past it. This asserted 1000 until the game's
        // own Mathf.Clamp(result, -999, 999) turned out to move it - so the package whose
        // whole job is to load last was quietly landing on the limit alongside whatever
        // else had overreached.
        Assert.Contains("<loadPriority>999</loadPriority>", xml);
        Assert.Equal(999, ElinTextureManager.Core.Model.PackageLimits.MaxLoadPriority);
        Assert.Contains("<title>", xml);
    }

    [Fact]
    public void SelectingCopiesTheTextureAndLeavesTheSourceUntouched()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "HD Characters", new[] { ("objC_2115.png", "hd-bytes") });
        var (scan, overrides, store) = Setup(ws);

        var source = scan.Index["objC_2115"].Versions[0];
        var sourceBytesBefore = File.ReadAllBytes(source.FullPath);

        var result = overrides.Select(source);

        Assert.True(result.Success);

        var copied = Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png");
        Assert.True(File.Exists(copied));
        Assert.Equal(sourceBytesBefore, File.ReadAllBytes(copied));

        // The Workshop file is byte-for-byte unchanged and still present.
        Assert.True(File.Exists(source.FullPath));
        Assert.Equal(sourceBytesBefore, File.ReadAllBytes(source.FullPath));

        var selection = store.Get("objC_2115");
        Assert.NotNull(selection);
        Assert.Equal("111", selection!.SourceWorkshopId);
        Assert.Equal("HD Characters", selection.SourceModName);
        Assert.NotNull(selection.SourceHash);
    }

    [Fact]
    public void SelectionsPersistAcrossStoreReloads()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, _) = Setup(ws);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        var reloaded = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        reloaded.Load();

        Assert.True(reloaded.Has("objC_2115"));
        Assert.Equal("Mod A", reloaded.Get("objC_2115")!.SourceModName);
    }

    [Fact]
    public void ChangingTheSelectionReplacesTheCopiedFile()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "aaa") });
        ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_2115.png", "bbb") });
        var (scan, overrides, store) = Setup(ws);

        var entry = scan.Index["objC_2115"];
        overrides.Select(entry.Versions.Single(v => v.ModName == "Mod A"));
        overrides.Select(entry.Versions.Single(v => v.ModName == "Mod B"));

        var copied = Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png");
        var modB = entry.Versions.Single(v => v.ModName == "Mod B");

        Assert.Equal(File.ReadAllBytes(modB.FullPath), File.ReadAllBytes(copied));
        Assert.Equal("Mod B", store.Get("objC_2115")!.SourceModName);
        Assert.Single(Directory.GetFiles(ws.Paths.OverrideTextureRoot, "*.png"));
    }

    [Fact]
    public void RemovingDeletesOnlyTheOverrideCopy()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, store) = Setup(ws);

        var source = scan.Index["objC_2115"].Versions[0];
        overrides.Select(source);

        var result = overrides.Remove("objC_2115");

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png")));
        Assert.False(store.Has("objC_2115"));

        // Source mod untouched.
        Assert.True(File.Exists(source.FullPath));
    }

    [Fact]
    public void ClearAllRemovesEveryOverrideButNoSourceFiles()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A",
            new[] { ("objC_1.png", "a"), ("objC_2.png", "b"), ("objC_3.png", "c") });
        var (scan, overrides, store) = Setup(ws);

        foreach (var id in new[] { "objC_1", "objC_2", "objC_3" })
            overrides.Select(scan.Index[id].Versions[0]);

        var (removed, failed) = overrides.ClearAll();

        Assert.Equal(3, removed);
        Assert.Equal(0, failed);
        Assert.Empty(Directory.GetFiles(ws.Paths.OverrideTextureRoot, "*.png"));
        Assert.Equal(0, store.Count);

        // All three sources survive.
        Assert.Equal(3, Directory.GetFiles(
            Path.Combine(ws.WorkshopRoot, "111", "Texture Replace"), "*.png").Length);
    }

    [Fact]
    public void AuditReportsWhenSteamUpdatedTheSourceTexture()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "version-one") });
        var (scan, overrides, _) = Setup(ws);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        // Simulate a Steam update changing the source bytes.
        var sourcePath = Path.Combine(ws.WorkshopRoot, "111", "Texture Replace", "objC_2115.png");
        TestWorkspace.WritePng(sourcePath, "version-two");

        var rescan = new ModScanner().ScanSynchronously(ws.Paths);
        var status = Assert.Single(overrides.Audit(rescan));

        Assert.Equal(OverrideState.SourceUpdated, status.State);
        Assert.True(status.FileExists);
    }

    [Fact]
    public void AuditReportsWhenTheSourceModIsGoneButKeepsTheOverride()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, _) = Setup(ws);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        // Simulate unsubscribing.
        Directory.Delete(Path.Combine(ws.WorkshopRoot, "111"), recursive: true);

        var rescan = new ModScanner().ScanSynchronously(ws.Paths);
        var status = Assert.Single(overrides.Audit(rescan));

        Assert.Equal(OverrideState.SourceMissing, status.State);
        Assert.True(File.Exists(Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2115.png")));
    }

    [Fact]
    public void AuditIsQuietWhenNothingChanged()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, _) = Setup(ws);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        var rescan = new ModScanner().ScanSynchronously(ws.Paths);

        Assert.Equal(OverrideState.Ok, Assert.Single(overrides.Audit(rescan)).State);
    }

    [Fact]
    public void OverridePackageIsPickedUpAsAnOverrideSourceOnRescan()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, _) = Setup(ws);

        overrides.Select(scan.Index["objC_2115"].Versions[0]);

        var rescan = new ModScanner().ScanSynchronously(ws.Paths);
        var entry = rescan.Index["objC_2115"];

        Assert.Contains(entry.Versions, v => v.SourceType == TextureSourceType.Override);
    }

    [Fact]
    public void SelectingAVariantWorksTheSameWay()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Outfit Pack", new[] { ("objC_2107.png", "default") });
        ws.AddVariant("111", "2_Regular_No Tights", "objC_2107.png", "no-tights");
        var (scan, overrides, _) = Setup(ws);

        var variant = Assert.Single(scan.Index["objC_2107"].Variants);
        Assert.True(overrides.Select(variant).Success);

        var copied = Path.Combine(ws.Paths.OverrideTextureRoot, "objC_2107.png");
        Assert.Equal(File.ReadAllBytes(variant.FullPath), File.ReadAllBytes(copied));
    }

    [Fact]
    public void MirrorToUserFolderCopiesAndRemovesAlongsideTheOverride()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_2115.png", "a") });
        var (scan, overrides, _) = Setup(ws);
        overrides.MirrorToUserFolder = true;

        overrides.Select(scan.Index["objC_2115"].Versions[0]);
        var mirrored = Path.Combine(ws.Paths.UserTextureReplace, "objC_2115.png");
        Assert.True(File.Exists(mirrored));

        overrides.Remove("objC_2115");
        Assert.False(File.Exists(mirrored));
    }
}

public class WinnerResolverTests
{
    private static (ScanResult scan, Dictionary<string, ModPackage> byKey) ScanWithOrder(
        TestWorkspace ws, params (string modDir, bool enabled)[] order)
    {
        ws.WriteLoadOrder(order);
        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        LoadOrderFile.ApplyTo(LoadOrderFile.Read(ws.Paths.LoadOrderFile), scan.Mods);
        return (scan, scan.Mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SingleSourceIsCertain()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Only Mod", new[] { ("objC_1.png", "a") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal(WinnerConfidence.Certain, winner.Confidence);
        Assert.Equal("Only Mod", winner.Description);
    }

    [Fact]
    public void LaterEntryWinsUnderTheDefaultConvention()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Early Mod", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("222", "Late Mod", new[] { ("objC_1.png", "b") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true), (b, true));

        var winner = new WinnerResolver(PriorityConvention.LaterWins)
            .Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal("Late Mod", winner.File!.ModName);
        Assert.Equal(WinnerConfidence.Likely, winner.Confidence);
    }

    [Fact]
    public void ConventionCanBeFlipped()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Early Mod", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("222", "Late Mod", new[] { ("objC_1.png", "b") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true), (b, true));

        var winner = new WinnerResolver(PriorityConvention.EarlierWins)
            .Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal("Early Mod", winner.File!.ModName);
    }

    [Fact]
    public void DisabledModsAreExcluded()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Enabled Mod", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("222", "Disabled Mod", new[] { ("objC_1.png", "b") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true), (b, false));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal("Enabled Mod", winner.File!.ModName);
        Assert.Equal(WinnerConfidence.Certain, winner.Confidence);
    }

    [Fact]
    public void IdenticalVersionsMakeTheWinnerCertain()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_1.png", "same") });
        var b = ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_1.png", "same") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true), (b, true));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal(WinnerConfidence.Certain, winner.Confidence);
        Assert.Contains("identical", winner.Description);
    }

    [Fact]
    public void SourcesMissingFromLoadOrderMakeTheResultUncertain()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Listed Mod", new[] { ("objC_1.png", "a") });
        ws.AddWorkshopMod("222", "Unlisted Mod", new[] { ("objC_1.png", "b") });
        var (scan, byKey) = ScanWithOrder(ws, (a, true));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.Equal(WinnerConfidence.Unknown, winner.Confidence);
        Assert.Contains("uncertain", winner.Description);
    }

    [Fact]
    public void TheOverridePackageWinsAndIsLabelledAsSuch()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("222", "Mod B", new[] { ("objC_1.png", "b") });

        ws.WriteLoadOrder((a, true), (b, true));
        var store = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, store);

        var first = new ModScanner().ScanSynchronously(ws.Paths);
        overrides.Select(first.Index["objC_1"].Versions.Single(v => v.ModName == "Mod A"));

        var (scan, byKey) = ScanWithOrder(ws, (a, true), (b, true));
        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.True(winner.IsManagerOverride);
        Assert.Equal("Overridden by Elin Texture Manager", winner.Description);
    }

    [Fact]
    public void VariantsNeverWin()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Outfit Pack", new[] { ("objC_1.png", "default") });
        ws.AddVariant("111", "unused", "objC_1.png", "alternate");
        var (scan, byKey) = ScanWithOrder(ws, (a, true));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.False(winner.File!.IsVariant);
    }

    [Fact]
    public void NoEnabledSourceYieldsNoWinner()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "Mod A", new[] { ("objC_1.png", "a") });
        var (scan, byKey) = ScanWithOrder(ws, (a, false));

        var winner = new WinnerResolver().Resolve(scan.Index["objC_1"], byKey);

        Assert.Null(winner.File);
    }
}
