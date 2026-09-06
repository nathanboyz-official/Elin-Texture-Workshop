using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.GameLog;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Reading the game's own log.
///
/// Every line below is taken verbatim out of a real Player.log, including the noise. The
/// noise is the point: a log of five thousand lines contains "error", "failed" and
/// "exception" many times over without anything being wrong, so the test that matters is
/// the one asserting silence.
/// </summary>
public sealed class PlayerLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("logs").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private List<HealthFinding> Read(params string[] lines)
    {
        var path = Path.Combine(_dir, "Player.log");
        File.WriteAllLines(path, lines);
        return PlayerLogReader.Read(path);
    }

    /// <summary>Real lines from a real log that mean nothing is wrong.</summary>
    private static readonly string[] Noise =
    {
        "Fallback handler could not load library C:/Program Files (x86)/Steam/steamapps/common/Elin/Elin_Data/MonoBleedingEdge/data-0000019FA488B120.dll",
        "ERROR: Shader Custom/UI/RadialBlur shader is not supported on this GPU (none of subshaders/fallbacks are suitable)",
        "Failed to load the Steam App List: State = ProtocolError, Error Message = HTTP/1.1 404 Not Found",
        "InvalidOperationException: Steamworks is not initialized.",
        "[CWL][INFO] [CustomAchievement] Loaded new achievement template 'cwl_first_exception'",
        "[Info   :KK GridStatus] ModConfigGUI not found - skipping GUI initialization",
        "Failed Allocations. Bucket layout:",
        "16B: 82 Subsections = 83968 buckets. Failed count: 173664",
        "[Info   :  PetFeats] Patch_RedirectPc: patched 4, failed 0.",
        "Chara mika not found",
    };

    [Fact]
    public void A_log_full_of_the_word_error_reports_nothing()
    {
        // Unity's own start-up, a shader the GPU will not take, a Steam timeout, an
        // achievement with "exception" in its name, and a patch tally of zero failures.
        Assert.Empty(Read(Noise));
    }

    [Fact]
    public void A_patch_that_did_not_apply_names_the_mod()
    {
        var findings = Read(Noise.Append(
            "[Warning:  PetFeats] patch failed <>c__DisplayClass94_0::<RefreshSkill>b__12: IL Compile Error (unknown location)")
            .ToArray());

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("PetFeats", finding.Title);
        Assert.Contains("PetFeats", finding.ModNames);
        Assert.Contains("IL Compile Error", string.Join(" ", finding.Evidence));
    }

    [Fact]
    public void A_mods_own_tally_of_failed_patches_is_reported()
    {
        var findings = Read("[Info   :  PetFeats] Patch_RedirectPc: patched 3, failed 1.");

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Notice, finding.Severity);
        Assert.Contains("1 of its patches failed", finding.Title);
    }

    [Fact]
    public void A_tally_with_no_failures_is_not_reported()
    {
        Assert.Empty(Read("[Info   :  PetFeats] Patch_RedirectPc: patched 4, failed 0."));
    }

    [Fact]
    public void The_same_plugin_installed_twice_is_reported_once()
    {
        var findings = Read(
            "[Warning:   BepInEx] Skipping [Micro_Bikini 1.0.0] because a newer version exists (Micro_Bikini 1.0.0)",
            "[Warning:   BepInEx] Skipping [Panty Thief 1.1.0] because a newer version exists (Panty Thief 1.1.0)");

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Conflict, finding.Severity);
        Assert.Contains("2 plugins", finding.Title);
        Assert.Contains("Micro_Bikini 1.0.0", finding.Evidence);
    }

    [Fact]
    public void A_plugin_listed_twice_is_still_one_finding()
    {
        var findings = Read(
            "[Warning:   BepInEx] Skipping [Micro_Bikini 1.0.0] because a newer version exists (Micro_Bikini 1.0.0)",
            "[Warning:   BepInEx] Skipping [Micro_Bikini 1.0.0] because a newer version exists (Micro_Bikini 1.0.0)");

        var finding = Assert.Single(findings);

        Assert.Contains("Micro_Bikini 1.0.0 is installed more than once", finding.Title);
    }

    [Fact]
    public void Bepinex_skipping_a_type_is_not_a_duplicate_plugin()
    {
        Assert.Empty(Read(
            "[Warning:   BepInEx] Skipping over type [yz_ReSS.ModConfigCore] as no metadata attribute is specified"));
    }

    [Fact]
    public void The_games_own_complaint_about_a_sheet_is_passed_on()
    {
        var findings = Read("#source ill-format file with missing columns, init with empty values");

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Notice, finding.Severity);
        Assert.Contains("missing columns", finding.Title);
    }

    [Fact]
    public void Missing_content_lines_are_deliberately_not_reported()
    {
        // 354 of these appeared in a real log. Every id was defined, reachable and owned
        // by an enabled mod, so whatever the game means by the line is not "this is
        // missing". Reporting it would be 354 findings nobody can act on.
        Assert.Empty(Read(
            "Chara mika not found",
            "Chara hoshino not found",
            "Thing my_sword not found"));
    }

    [Fact]
    public void A_log_that_is_not_there_is_not_a_crash()
    {
        Assert.Empty(PlayerLogReader.Read(Path.Combine(_dir, "nothing.log")));
    }

    [Fact]
    public void A_log_held_open_by_the_running_game_can_still_be_read()
    {
        var path = Path.Combine(_dir, "open.log");
        File.WriteAllLines(path, new[] { "#source ill-format file with missing columns" });

        // The game keeps its log open for writing the whole time it runs.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        Assert.Single(PlayerLogReader.Read(path));
    }
}
