using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Where the game really puts a mod. ModManager.LoadLoadOrder replaces every listed mod's
/// package.xml loadPriority with its line in loadorder.txt before sorting, so for those the
/// line is everything and package.xml is nothing.
/// </summary>
public sealed class GamePriorityTests
{
    private static HealthReport Health(TestWorkspace ws)
    {
        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        var order = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(order, scan.Mods);
        return new HealthScanner().Scan(ws.Paths, scan, order);
    }

    private static OverrideManager ChooseFrom(TestWorkspace ws, string modName, string textureId)
    {
        var store = new SelectionStore(Path.Combine(ws.Root, "selections.json"));
        var overrides = new OverrideManager(ws.Paths, store);
        var scan = new ModScanner().ScanSynchronously(ws.Paths);
        Assert.True(overrides.Select(scan.Index[textureId].Versions.Single(v => v.ModName == modName)).Success);
        return overrides;
    }

    [Fact]
    public void A_listed_mod_asking_for_too_much_is_not_reported()
    {
        // Its package.xml number is thrown away in favour of its line, so the clamp
        // never happens to it.
        using var ws = new TestWorkspace();
        var greedy = ws.AddWorkshopMod("100", "Greedy", new[] { ("chara_x.png", "a") }, loadPriority: 114514);
        ws.WriteLoadOrder((greedy, true));

        var report = Health(ws);

        Assert.DoesNotContain(report.Findings, f => f.Check == HealthScanner.PriorityCheck);
    }

    [Fact]
    public void An_unlisted_mod_asking_for_too_much_is_reported()
    {
        using var ws = new TestWorkspace();
        var plain = ws.AddWorkshopMod("100", "Plain", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Greedy", new[] { ("chara_y.png", "b") }, loadPriority: 114514);
        ws.WriteLoadOrder((plain, true));

        var report = Health(ws);

        var finding = Assert.Single(report.Findings, f => f.Check == HealthScanner.PriorityCheck);
        Assert.Contains("Greedy", finding.ModNames);
    }

    [Fact]
    public void A_mod_loading_after_the_choices_is_reported()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("100", "Mod A", new[] { ("objC_1.png", "a") });
        var late = ws.AddWorkshopMod("200", "Late Mod", new[] { ("objC_1.png", "late") });
        ChooseFrom(ws, "Mod A", "objC_1");
        ws.WriteLoadOrder((a, true), (ws.Paths.OverridePackageRoot, true), (late, true));

        var report = Health(ws);

        var finding = Assert.Single(report.Findings, f => f.Check == HealthScanner.OverrideOrderCheck);
        Assert.Equal(HealthSeverity.Conflict, finding.Severity);
        Assert.Contains("Late Mod", finding.ModNames);
        Assert.Contains(finding.Evidence, e => e.StartsWith("objC_1"));
    }

    [Fact]
    public void Choices_at_the_bottom_are_not_reported()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("100", "Mod A", new[] { ("objC_1.png", "a") });
        var other = ws.AddWorkshopMod("200", "Other Mod", new[] { ("objC_1.png", "b") });
        ChooseFrom(ws, "Mod A", "objC_1");
        ws.WriteLoadOrder((a, true), (other, true), (ws.Paths.OverridePackageRoot, true));

        var report = Health(ws);

        Assert.DoesNotContain(report.Findings, f => f.Check == HealthScanner.OverrideOrderCheck);
    }

    [Fact]
    public void Saving_puts_the_override_package_back_at_the_bottom()
    {
        using var ws = new TestWorkspace();
        var a = ws.AddWorkshopMod("100", "Mod A", new[] { ("objC_1.png", "a") });
        var late = ws.AddWorkshopMod("200", "Late Mod", new[] { ("objC_1.png", "late") });
        ws.WriteLoadOrder((a, true), (ws.Paths.OverridePackageRoot, true), (late, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        Assert.True(LoadOrderFile.Save(doc, ws.BackupDirectory, out _));

        var lines = File.ReadAllLines(ws.Paths.LoadOrderFile);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith(ws.Paths.OverridePackageRoot, lines[^1]);
        Assert.StartsWith(late, lines[1]);
    }

    [Fact]
    public void A_package_xml_written_with_1000_is_brought_back_to_999()
    {
        using var ws = new TestWorkspace();
        var overrides = new OverrideManager(ws.Paths,
            new SelectionStore(Path.Combine(ws.Root, "selections.json")));
        Assert.True(overrides.EnsurePackage(loadPriority: 1000).Success);

        Assert.True(overrides.RepairPackageXml());

        var xml = File.ReadAllText(ws.Paths.OverridePackageXml);
        Assert.Contains("<loadPriority>999</loadPriority>", xml);
        Assert.Contains("<title>Elin Texture Manager Overrides</title>", xml);
        Assert.False(overrides.RepairPackageXml());
    }

    [Fact]
    public void Repairing_without_a_package_creates_nothing()
    {
        using var ws = new TestWorkspace();
        var overrides = new OverrideManager(ws.Paths,
            new SelectionStore(Path.Combine(ws.Root, "selections.json")));

        Assert.False(overrides.RepairPackageXml());
        Assert.False(Directory.Exists(ws.Paths.OverridePackageRoot));
    }

    [Fact]
    public void A_saved_setting_of_1000_loads_as_999()
    {
        using var ws = new TestWorkspace();
        var path = Path.Combine(ws.Root, "settings.json");
        File.WriteAllText(path, "{ \"OverrideLoadPriority\": 1000 }");

        Assert.Equal(999, AppSettings.Load(path).OverrideLoadPriority);
    }
}
