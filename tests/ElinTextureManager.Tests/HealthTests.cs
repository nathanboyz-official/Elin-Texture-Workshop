using System.Text;
using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The health checks, which exist because Elin's own crash dialog blames the wrong mod:
/// it lists the Harmony patches wrapping a failed call, so the innocent mod at the top
/// of the trace is the one people uninstall.
///
/// The checks that read IL are exercised against real assemblies - this test assembly
/// and the Core one - because a hand-written stub .dll would only prove the code can
/// reject garbage.
/// </summary>
public sealed class HealthTests
{
    private static HealthReport Run(TestWorkspace ws)
    {
        var scan = new ModScanner(new ScanOptions { ComputeHashes = false })
            .ScanSynchronously(ws.Paths);
        var order = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(order, scan.Mods);
        return new HealthScanner().Scan(ws.Paths, scan, order);
    }

    /// <summary>The assembly this test lives in - a real managed DLL to plant in mods.</summary>
    private static string RealAssembly => typeof(HealthTests).Assembly.Location;

    private static string SecondRealAssembly => typeof(HealthScanner).Assembly.Location;

    private static void PlantAssembly(TestWorkspace ws, string workshopId, string fileName)
    {
        var target = Path.Combine(ws.WorkshopRoot, workshopId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(RealAssembly, target, overwrite: true);
    }

    [Fact]
    public void A_library_of_plain_texture_mods_has_nothing_wrong_with_it()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Texture Pack", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Another Pack", new[] { ("chara_y.png", "b") });
        ws.WriteLoadOrder(
            (Path.Combine(ws.WorkshopRoot, "100"), true),
            (Path.Combine(ws.WorkshopRoot, "200"), true));

        var report = Run(ws);

        Assert.True(report.IsClean, string.Join(" | ", report.Findings.Select(f => f.Title)));
        Assert.Equal(0, report.CodeModCount);
    }

    [Fact]
    public void A_mod_with_no_assemblies_is_not_counted_as_shipping_code()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Texture Pack", new[] { ("chara_x.png", "a") });

        Assert.Equal(0, Run(ws).CodeModCount);
    }

    [Fact]
    public void A_file_named_dll_that_is_not_an_assembly_is_ignored()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Native Bits", new[] { ("chara_x.png", "a") });
        File.WriteAllText(Path.Combine(ws.WorkshopRoot, "100", "native.dll"), "not an assembly");
        ws.WriteLoadOrder((Path.Combine(ws.WorkshopRoot, "100"), true));

        var report = Run(ws);

        Assert.Equal(0, report.CodeModCount);
        Assert.True(report.IsClean);
    }

    [Fact]
    public void Two_mods_shipping_the_same_assembly_name_is_broken()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Texture Expand", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Provisional Texture Expand", new[] { ("chara_y.png", "b") });
        PlantAssembly(ws, "100", "TextureExpand.dll");
        PlantAssembly(ws, "200", "TextureExpand.dll");

        var report = Run(ws);

        var finding = Assert.Single(report.Findings,
            f => f.Check == HealthScanner.DuplicateCheck);

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("TextureExpand.dll", finding.Title);
        Assert.Equal(2, finding.ModKeys.Count);
        Assert.Contains("Texture Expand", finding.ModNames);
        Assert.Contains("Provisional Texture Expand", finding.ModNames);
    }

    [Fact]
    public void One_mod_shipping_an_assembly_twice_is_not_a_duplicate()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Solo", new[] { ("chara_x.png", "a") });
        PlantAssembly(ws, "100", "Thing.dll");
        PlantAssembly(ws, "100", Path.Combine("backup", "Thing.dll"));

        // Same file name, one owner. Shipping your own backup copy is untidy, not broken.
        Assert.DoesNotContain(Run(ws).Findings, f => f.Check == HealthScanner.DuplicateCheck);
    }

    [Fact]
    public void Load_order_lines_pointing_at_folders_that_are_gone_are_reported()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Present", new[] { ("chara_x.png", "a") });
        ws.WriteLoadOrder(
            (Path.Combine(ws.WorkshopRoot, "100"), true),
            (Path.Combine(ws.WorkshopRoot, "999999"), true));

        var finding = Assert.Single(Run(ws).Findings,
            f => f.Check == HealthScanner.LoadOrderCheck);

        Assert.Equal(HealthSeverity.Notice, finding.Severity);
        Assert.Contains("gone", finding.Title);
        Assert.Contains("999999", string.Join("\n", finding.Evidence));
    }

    [Fact]
    public void A_load_order_line_spelt_differently_from_disk_is_broken()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "Present", new[] { ("chara_x.png", "a") });

        // Windows opens either spelling, so the mod scans fine and the UI looks right.
        // Elin compares the string, so this line is one it silently never matches.
        File.WriteAllText(ws.Paths.LoadOrderFile, dir.ToLowerInvariant() + ",0\n",
            new UTF8Encoding(false));

        var finding = Assert.Single(Run(ws).Findings,
            f => f.Check == HealthScanner.LoadOrderCheck);

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("spelt differently", finding.Title);
    }

    [Fact]
    public void A_missing_load_order_file_is_only_a_notice()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Present", new[] { ("chara_x.png", "a") });

        var finding = Assert.Single(Run(ws).Findings,
            f => f.Check == HealthScanner.LoadOrderCheck);

        Assert.Equal(HealthSeverity.Notice, finding.Severity);
    }

    [Fact]
    public void A_missing_game_assembly_disables_the_api_check_rather_than_the_scan()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Coded", new[] { ("chara_x.png", "a") });
        PlantAssembly(ws, "100", "Coded.dll");

        var report = Run(ws);

        // The stub workspace has no Elin.dll, which must not be mistaken for "all clear".
        Assert.NotNull(report.GameAssemblyError);
        Assert.Equal(0, report.GameMethodCount);
        Assert.DoesNotContain(report.Findings, f => f.Check == HealthScanner.ApiCheck);
        Assert.Equal(1, report.CodeModCount);
    }

    [Fact]
    public void Calls_that_match_the_games_signatures_are_not_reported()
    {
        using var ws = new TestWorkspace();

        // Core stands in for Elin.dll, and this test assembly stands in for a mod. It
        // really does call into Core, and every call compiles against the copy on disk,
        // so a finding here would be a false positive - the failure mode that would
        // make this page worth ignoring.
        var managed = Path.Combine(ws.ElinRoot, "Elin_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.Copy(SecondRealAssembly, Path.Combine(managed, "Elin.dll"));

        ws.AddWorkshopMod("100", "Coded", new[] { ("chara_x.png", "a") });
        PlantAssembly(ws, "100", "Coded.dll");

        var report = Run(ws);

        Assert.Null(report.GameAssemblyError);
        Assert.True(report.GameMethodCount > 0);
        Assert.DoesNotContain(report.Findings, f => f.Check == HealthScanner.ApiCheck);
    }

    [Fact]
    public void Defined_and_Referenced_describe_the_same_method_the_same_way()
    {
        // The API check compares these two sets as strings, so the formats have to agree.
        var defined = AssemblyIndex.Defined(SecondRealAssembly);
        var referenced = AssemblyIndex.Referenced(RealAssembly);

        var declared = Assert.Single(defined,
            m => m.Type == "HealthScanner" && m.Name == "Scan");

        Assert.Contains(referenced,
            m => m.Type == "HealthScanner" && m.Name == "Scan" && m.Key == declared.Key);
    }

    [Fact]
    public void A_file_that_is_not_an_assembly_reads_as_unmanaged_rather_than_throwing()
    {
        using var ws = new TestWorkspace();
        var junk = Path.Combine(ws.Root, "native.dll");
        File.WriteAllBytes(junk, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 });

        Assert.False(AssemblyIndex.IsManaged(junk));
        Assert.Empty(AssemblyIndex.Defined(junk));
        Assert.Empty(AssemblyIndex.Referenced(junk));
        Assert.Empty(AssemblyIndex.PatchTargets(junk));
    }

    [Fact]
    public void Overlapping_patches_are_one_notice_rather_than_one_alarm_per_method()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "A", new[] { ("chara_x.png", "a") });
        PlantAssembly(ws, "100", "A.dll");
        ws.AddWorkshopMod("200", "B", new[] { ("chara_y.png", "b") });
        PlantAssembly(ws, "200", "B.dll");

        var patchFindings = Run(ws).Findings
            .Where(f => f.Check == HealthScanner.PatchCheck)
            .ToList();

        // Mods sharing a patch target is ordinary; a real library has dozens. Raising
        // each as its own finding would bury the ones that are actually broken.
        Assert.True(patchFindings.Count <= 1);
        Assert.All(patchFindings, f => Assert.Equal(HealthSeverity.Notice, f.Severity));
    }

    /// <summary>
    /// Every dependency line the report produced.
    ///
    /// The tests assert on the one reference they planted rather than on there being no
    /// findings at all, because the stand-in mod is this test assembly and it also
    /// references xunit - which is noise from the harness, not the product.
    /// </summary>
    private static string DependencyEvidence(HealthReport report) =>
        string.Join("\n", report.Findings
            .Where(f => f.Check == HealthScanner.DependencyCheck)
            .SelectMany(f => f.Evidence));

    /// <summary>Puts an assembly where the game keeps its own, so it counts as stock.</summary>
    private static void PlantGameAssembly(TestWorkspace ws, string fileName)
    {
        var managed = Path.Combine(ws.ElinRoot, "Elin_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.Copy(RealAssembly, Path.Combine(managed, fileName), overwrite: true);
    }

    [Fact]
    public void A_mod_built_against_another_mod_that_is_installed_is_not_reported()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Framework", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Dependent", new[] { ("chara_y.png", "b") });

        // The dependent mod is this test assembly, which really does reference Core; the
        // framework mod ships Core. That is a satisfied dependency, in real metadata.
        File.Copy(SecondRealAssembly,
            Path.Combine(ws.WorkshopRoot, "100", Path.GetFileName(SecondRealAssembly)));
        PlantAssembly(ws, "200", "Dependent.dll");
        ws.WriteLoadOrder(
            (Path.Combine(ws.WorkshopRoot, "100"), true),
            (Path.Combine(ws.WorkshopRoot, "200"), true));

        Assert.DoesNotContain("ElinTextureManager.Core", DependencyEvidence(Run(ws)));
    }

    [Fact]
    public void A_mod_whose_dependency_is_nowhere_is_broken()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("200", "Dependent", new[] { ("chara_y.png", "b") });
        PlantAssembly(ws, "200", "Dependent.dll");
        ws.WriteLoadOrder((Path.Combine(ws.WorkshopRoot, "200"), true));

        var report = Run(ws);
        var finding = Assert.Single(report.Findings,
            f => f.Check == HealthScanner.DependencyCheck && f.Title.Contains("not installed"));

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("ElinTextureManager.Core", DependencyEvidence(report));
    }

    [Fact]
    public void Assemblies_the_game_itself_ships_are_not_missing_dependencies()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("200", "Dependent", new[] { ("chara_y.png", "b") });
        PlantAssembly(ws, "200", "Dependent.dll");
        ws.WriteLoadOrder((Path.Combine(ws.WorkshopRoot, "200"), true));

        // Every mod is built against a stack the game provides. Reading only mod folders
        // reported 44 broken mods on a real library instead of 6.
        PlantGameAssembly(ws, Path.GetFileName(SecondRealAssembly));

        Assert.DoesNotContain("ElinTextureManager.Core", DependencyEvidence(Run(ws)));
    }

    [Fact]
    public void Turning_off_a_mod_that_another_one_needs_is_reported()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Framework", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Dependent", new[] { ("chara_y.png", "b") });
        File.Copy(SecondRealAssembly,
            Path.Combine(ws.WorkshopRoot, "100", Path.GetFileName(SecondRealAssembly)));
        PlantAssembly(ws, "200", "Dependent.dll");

        // Switching one thing off can break something you did not touch, which is the
        // whole reason to say so here rather than let the game fail silently.
        ws.WriteLoadOrder(
            (Path.Combine(ws.WorkshopRoot, "100"), false),
            (Path.Combine(ws.WorkshopRoot, "200"), true));

        var finding = Assert.Single(Run(ws).Findings,
            f => f.Check == HealthScanner.DependencyCheck && f.Title.Contains("turned off"));

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("Framework", string.Join("\n", finding.Evidence));
    }

    [Fact]
    public void A_mod_that_is_itself_off_is_not_warned_about_its_dependencies()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Framework", new[] { ("chara_x.png", "a") });
        ws.AddWorkshopMod("200", "Dependent", new[] { ("chara_y.png", "b") });
        File.Copy(SecondRealAssembly,
            Path.Combine(ws.WorkshopRoot, "100", Path.GetFileName(SecondRealAssembly)));
        PlantAssembly(ws, "200", "Dependent.dll");

        // Both off. Nothing is going to run, so nothing is going to break.
        ws.WriteLoadOrder(
            (Path.Combine(ws.WorkshopRoot, "100"), false),
            (Path.Combine(ws.WorkshopRoot, "200"), false));

        Assert.DoesNotContain(Run(ws).Findings, f => f.Title.Contains("turned off"));
    }
}
