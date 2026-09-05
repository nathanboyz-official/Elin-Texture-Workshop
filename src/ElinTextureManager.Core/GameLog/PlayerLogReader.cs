using System.Text.RegularExpressions;
using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.GameLog;

/// <summary>
/// Reads Elin's own log and turns the few lines that matter into findings.
///
/// The game already writes down most of what goes wrong. Nobody reads it, and reasonably
/// so: the log is five thousand lines of Unity start-up, shader compilation and memory
/// accounting, it is not even kept under the game's folder, and the handful of lines that
/// mean something are not marked out in any way.
///
/// Only specific shapes are matched, because the alternative does not work. Searching for
/// "error", "failed" or "exception" over a real log returns Unity's fallback library
/// loader, its allocation report at shutdown, a Steam network timeout, the game's own
/// unsupported shaders, and an achievement one mod defines called "cwl_first_exception".
/// None of those is a problem and all of them contain the word.
///
/// What is deliberately NOT read: the "Chara &lt;id&gt; not found" lines, of which a real
/// log had 354. Every one of those ids turned out to be defined, reachable and owned by
/// an enabled mod, so whatever the game means by it is not "this content is missing" -
/// and a check that reports 354 things it cannot explain is worse than no check. If the
/// meaning is ever established, this is the place for it.
/// </summary>
public static class PlayerLogReader
{
    public const string CheckName = "Game log";

    /// <summary>"[Warning:  PetFeats] patch failed &lt;method&gt;: IL Compile Error".</summary>
    private static readonly Regex PatchFailed = new(
        @"^\[Warning:\s*(?<mod>[^\]]+?)\s*\] patch failed (?<what>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>"[Info :  PetFeats] Patch_RedirectPc: patched 3, failed 1."</summary>
    private static readonly Regex PatchTally = new(
        @"^\[\w+\s*:\s*(?<mod>[^\]]+?)\s*\] (?<name>\S+): patched (?<ok>\d+), failed (?<bad>\d+)\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>"[Warning: BepInEx] Skipping [Micro_Bikini 1.0.0] because a newer version exists".</summary>
    private static readonly Regex SkippedPlugin = new(
        @"^\[Warning:\s*BepInEx\s*\] Skipping \[(?<name>[^\]]+)\] because a newer version exists",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads the log at the given path. Missing or unreadable gives nothing.</summary>
    public static List<HealthFinding> Read(string path)
    {
        var findings = new List<HealthFinding>();
        var lines = Lines(path);
        if (lines.Count == 0) return findings;

        PatchFindings(lines, path, findings);
        DuplicatePluginFindings(lines, path, findings);
        SheetFormatFindings(lines, path, findings);

        return findings;
    }

    /// <summary>
    /// The log is open in the running game, so it is read shared. A locked log is a log
    /// this declines to read rather than a crash.
    /// </summary>
    private static List<string> Lines(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<string>();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line.TrimEnd());

            return lines;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read {path}: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>A Harmony patch that did not apply. That part of the mod is simply off.</summary>
    private static void PatchFindings(List<string> lines, string path, List<HealthFinding> findings)
    {
        foreach (var line in lines)
        {
            var failed = PatchFailed.Match(line);

            if (failed.Success)
            {
                var mod = failed.Groups["mod"].Value.Trim();

                var finding = new HealthFinding
                {
                    Severity = HealthSeverity.Broken,
                    Check = CheckName,
                    Title = $"{mod} could not apply one of its patches",
                    Detail = "The mod loaded but one of the changes it makes to the game "
                             + "did not take. Whatever that patch was for does not happen, "
                             + "and the game carries on as though the mod were not there "
                             + "for that one thing. The game said so in its own log.",
                    Suggestion = "Usually means the mod is built for a different version of "
                                 + "Elin. Check for an update, or turn it off if the "
                                 + "feature matters.",
                };

                finding.ModNames.Add(mod);
                finding.Evidence.Add($"{Name(path)} · {failed.Groups["what"].Value.Trim()}");
                findings.Add(finding);
                continue;
            }

            var tally = PatchTally.Match(line);
            if (!tally.Success) continue;
            if (tally.Groups["bad"].Value == "0") continue;

            var owner = tally.Groups["mod"].Value.Trim();

            var counted = new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = CheckName,
                Title = $"{owner} reports {tally.Groups["bad"].Value} of its patches failed",
                Detail = "The mod counts its own patches in the log, and some of them did "
                         + "not apply. Part of what it changes is not in effect.",
                Suggestion = "Check for an update to this mod.",
            };

            counted.ModNames.Add(owner);
            counted.Evidence.Add($"{Name(path)} · {line.Trim()}");
            findings.Add(counted);
        }
    }

    /// <summary>The same plugin installed twice. One copy is ignored.</summary>
    private static void DuplicatePluginFindings(List<string> lines, string path,
        List<HealthFinding> findings)
    {
        var skipped = new List<string>();

        foreach (var line in lines)
        {
            var match = SkippedPlugin.Match(line);
            if (match.Success) skipped.Add(match.Groups["name"].Value.Trim());
        }

        skipped = skipped.Distinct(StringComparer.Ordinal).ToList();
        if (skipped.Count == 0) return;

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Conflict,
            Check = CheckName,
            Title = skipped.Count == 1
                ? $"{skipped[0]} is installed more than once"
                : $"{skipped.Count} plugins are installed more than once",
            Detail = "BepInEx found the same plugin twice, loaded one copy and skipped the "
                     + "other. Two mods shipping the same plugin, or an old manual install "
                     + "left beside a Workshop one.",
            Suggestion = "Find the older copy and remove it, so which one runs is your "
                         + "choice rather than whichever BepInEx happened to prefer.",
        };

        finding.Evidence.Add(Name(path));
        finding.Evidence.AddRange(skipped.Take(10));
        findings.Add(finding);
    }

    /// <summary>The game's own complaint about a malformed source sheet.</summary>
    private static void SheetFormatFindings(List<string> lines, string path,
        List<HealthFinding> findings)
    {
        var count = lines.Count(l => l.Contains("#source ill-format", StringComparison.Ordinal));
        if (count == 0) return;

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = CheckName,
            Title = count == 1
                ? "A source sheet is missing columns"
                : $"{count} source sheets are missing columns",
            Detail = "The game found a sheet whose header does not have every column it "
                     + "expects, and filled the missing ones with nothing. Whatever those "
                     + "columns controlled is at its default rather than at what the "
                     + "author intended.",
            Suggestion = "Copy the first three rows of the official sheet in whole - the "
                         + "header, the types and the defaults.",
        };

        finding.Evidence.Add($"{Name(path)} · the game does not say which file");
        findings.Add(finding);
    }

    private static string Name(string path) => Path.GetFileName(path);
}
