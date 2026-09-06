using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// loadorder.txt is read back by Elin, which compares the path strings to find each
/// mod. Windows opens a path whatever its case, so a mis-cased line still scans, still
/// looks right in this application, and is still ignored by the game - a mod switched
/// off here would quietly stay switched on in play. These tests pin the spelling.
/// </summary>
public sealed class PathCasingTests
{
    [Fact]
    public void TrueCase_returns_the_spelling_on_disk()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });

        var shouted = dir.ToUpperInvariant();

        Assert.NotEqual(shouted, dir);                       // the test is testing something
        Assert.Equal(dir, SafePath.TrueCase(shouted));
    }

    [Fact]
    public void TrueCase_leaves_a_path_that_is_not_on_disk_alone()
    {
        using var ws = new TestWorkspace();
        var missing = Path.Combine(ws.WorkshopRoot, "no-such-mod");

        // Nothing to resolve against, so it must not invent or mangle anything.
        Assert.Equal(missing, SafePath.TrueCase(missing));
    }

    [Fact]
    public void An_appended_entry_uses_the_on_disk_spelling()
    {
        using var ws = new TestWorkspace();
        var listed = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        var unlisted = ws.AddWorkshopMod("200", "B", new[] { ("objC_2.png", "b") });
        ws.WriteLoadOrder((listed, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        // Disable it via a differently-cased path, as a remembered setting would give.
        Assert.True(LoadOrderFile.SetEnabled(doc, unlisted.ToUpperInvariant(), false));

        Assert.Equal(unlisted, doc.Entries[1].Path);
    }

    [Fact]
    public void Saving_repairs_an_entry_that_is_only_mis_cased()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });

        // A file written by an older build, with the wrong case.
        File.WriteAllText(ws.Paths.LoadOrderFile, dir.ToUpperInvariant() + ",0\r\n");

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        Assert.True(LoadOrderFile.Save(doc, ws.BackupDirectory, out _));

        var written = File.ReadAllText(ws.Paths.LoadOrderFile).Trim();

        Assert.Equal(dir + ",0", written);
    }

    [Fact]
    public void Repairing_the_case_never_changes_the_enabled_flags()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        var b = ws.AddWorkshopMod("200", "B", new[] { ("objC_2.png", "b") });

        File.WriteAllText(ws.Paths.LoadOrderFile,
            a.ToUpperInvariant() + ",0\r\n" + b.ToUpperInvariant() + ",1\r\n");

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.Save(doc, ws.BackupDirectory, out _);

        var lines = File.ReadAllLines(ws.Paths.LoadOrderFile)
            .Where(l => l.Length > 0).ToArray();

        Assert.Equal(a + ",0", lines[0]);
        Assert.Equal(b + ",1", lines[1]);
    }

    [Fact]
    public void An_unparsed_line_is_never_re_cased()
    {
        using var ws = new TestWorkspace();
        File.WriteAllText(ws.Paths.LoadOrderFile, "SOMETHING WE DO NOT UNDERSTAND\r\n");

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.Save(doc, ws.BackupDirectory, out _);

        Assert.Equal("SOMETHING WE DO NOT UNDERSTAND",
            File.ReadAllText(ws.Paths.LoadOrderFile).Trim());
    }

    [Fact]
    public void A_disabled_mod_survives_a_scan_when_the_stored_root_is_mis_cased()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        ws.WriteLoadOrder((dir, false));

        // Paths remembered in settings can carry any case; the scan must still match.
        var paths = ElinTextureManager.Core.Detection.ElinPaths.FromElinRoot(
            ws.ElinRoot.ToUpperInvariant(), ws.WorkshopRoot.ToUpperInvariant());

        var scan = new ModScanner(new ScanOptions { ComputeHashes = false }).ScanSynchronously(paths);
        var doc = LoadOrderFile.Read(paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(doc, scan.Mods);

        Assert.False(Assert.Single(scan.Mods, m => m.Key == "100").Enabled);
    }
}
