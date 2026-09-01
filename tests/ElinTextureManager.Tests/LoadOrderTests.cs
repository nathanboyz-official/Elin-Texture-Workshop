using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

public class LoadOrderTests
{
    [Fact]
    public void ParsesThePathCommaFlagFormat()
    {
        using var ws = new TestWorkspace();
        var a = Path.Combine(ws.WorkshopRoot, "3427330411");
        var b = Path.Combine(ws.WorkshopRoot, "3381182341");
        ws.WriteLoadOrder((a, true), (b, false));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.Equal(2, doc.Entries.Count);
        Assert.Equal(a, doc.Entries[0].Path);
        Assert.True(doc.Entries[0].Enabled);
        Assert.False(doc.Entries[1].Enabled);
    }

    [Fact]
    public void PreservesUnrecognisedLinesVerbatim()
    {
        using var ws = new TestWorkspace();
        File.WriteAllText(ws.Paths.LoadOrderFile,
            "C:\\mods\\a,1\r\nsomething unexpected\r\nC:\\mods\\b,0\r\n");

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.Equal(3, doc.Entries.Count);
        Assert.False(doc.Entries[1].IsParsed);
        Assert.Equal("something unexpected", doc.Entries[1].Serialize());
    }

    [Fact]
    public void MissingFileYieldsAnEmptyDocumentRatherThanThrowing()
    {
        using var ws = new TestWorkspace();

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.Empty(doc.Entries);
        Assert.False(doc.Exists);
    }

    [Fact]
    public void SaveCreatesABackupFirst()
    {
        using var ws = new TestWorkspace();
        var a = Path.Combine(ws.WorkshopRoot, "111");
        ws.WriteLoadOrder((a, true));
        var original = File.ReadAllText(ws.Paths.LoadOrderFile);

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        doc.Entries[0].Enabled = false;

        var saved = LoadOrderFile.Save(doc, ws.BackupDirectory, out var backupPath);

        Assert.True(saved);
        Assert.NotNull(backupPath);
        Assert.Equal(original, File.ReadAllText(backupPath!));
        Assert.Contains(",0", File.ReadAllText(ws.Paths.LoadOrderFile));
    }

    [Fact]
    public void RoundTripsWithoutLosingEntries()
    {
        using var ws = new TestWorkspace();
        var entries = Enumerable.Range(0, 50)
            .Select(i => (Path.Combine(ws.WorkshopRoot, $"mod{i}"), i % 3 != 0))
            .ToArray();
        ws.WriteLoadOrder(entries);

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.Save(doc, ws.BackupDirectory, out _);
        var reread = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.Equal(50, reread.Entries.Count);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(doc.Entries[i].Path, reread.Entries[i].Path);
            Assert.Equal(doc.Entries[i].Enabled, reread.Entries[i].Enabled);
        }
    }

    [Fact]
    public void RestoreBringsBackAPreviousVersion()
    {
        using var ws = new TestWorkspace();
        var a = Path.Combine(ws.WorkshopRoot, "111");
        ws.WriteLoadOrder((a, true));
        var original = File.ReadAllText(ws.Paths.LoadOrderFile);

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        doc.Entries[0].Enabled = false;
        LoadOrderFile.Save(doc, ws.BackupDirectory, out var backup);

        var restored = LoadOrderFile.Restore(backup!, ws.Paths, ws.BackupDirectory);

        Assert.True(restored);
        Assert.Equal(original, File.ReadAllText(ws.Paths.LoadOrderFile));
    }

    [Fact]
    public void RestoreRefusesFilesOutsideTheBackupFolder()
    {
        using var ws = new TestWorkspace();
        ws.WriteLoadOrder((Path.Combine(ws.WorkshopRoot, "111"), true));

        var rogue = Path.Combine(ws.Root, "rogue.txt");
        File.WriteAllText(rogue, "C:\\evil,1\r\n");

        Assert.False(LoadOrderFile.Restore(rogue, ws.Paths, ws.BackupDirectory));
    }

    [Fact]
    public void AppliesPositionAndEnabledStateOntoScannedMods()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("111", "First", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("222", "Second", new[] { ("objC_2.png", "b") });
        ws.WriteLoadOrder((a, true), (b, false));

        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(doc, scan.Mods);

        var first = scan.Mods.Single(m => m.WorkshopId == "111");
        var second = scan.Mods.Single(m => m.WorkshopId == "222");

        Assert.Equal(0, first.LoadOrderIndex);
        Assert.True(first.Enabled);
        Assert.True(first.InLoadOrderFile);

        Assert.Equal(1, second.LoadOrderIndex);
        Assert.False(second.Enabled);
    }

    [Fact]
    public void ModsAbsentFromTheFileAreTreatedAsEnabledAndUnordered()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("111", "Listed", new[] { ("objC_1.png", "a") });
        ws.AddWorkshopMod("222", "Unlisted", new[] { ("objC_2.png", "b") });
        ws.WriteLoadOrder((Path.Combine(ws.WorkshopRoot, "111"), true));

        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        LoadOrderFile.ApplyTo(LoadOrderFile.Read(ws.Paths.LoadOrderFile), scan.Mods);

        var unlisted = scan.Mods.Single(m => m.WorkshopId == "222");

        Assert.Equal(-1, unlisted.LoadOrderIndex);
        Assert.False(unlisted.InLoadOrderFile);
        Assert.True(unlisted.Enabled);
    }
}
