using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Sheets;

/// <summary>
/// Reads a mod's source sheets and reports the mistakes the game will not.
///
/// Elin loads these sheets quietly. A single row with no id throws away every row beneath
/// it - the mod loads, the game starts, and the content is simply absent, with nothing
/// said anywhere.
///
/// Only tabs the game reads as source data are judged, and only in workbooks that hold at
/// least one of them. That restraint was learned rather than chosen: judging every tab
/// produced 270 findings against a real library and every one of them was wrong. Mods
/// carry plenty of other spreadsheets - dialog.xlsx, Name.xlsx, Alias.xlsx, chara_talk.xlsx
/// - whose tabs are named by their own conventions ("general", "rumor", "Alias_EN", "1")
/// and are none of this checker's business. A check that cries wolf about the base game's
/// own files is worse than no check.
/// </summary>
public static class SourceSheetChecker
{
    public const string CheckName = "Source sheet";

    /// <summary>The column that decides whether a row exists at all.</summary>
    public const string IdColumn = "id";

    /// <summary>Checks one workbook. The mod name is only used to name the culprit.</summary>
    public static List<HealthFinding> Check(string path, string modName, string? modKey = null)
    {
        var findings = new List<HealthFinding>();
        List<XlsxSheet> sheets;

        try
        {
            sheets = XlsxWorkbook.Read(path);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read source sheet {path}: {ex.Message}");
            return findings;
        }

        // Only a workbook that already holds source data is one of these. Anything else
        // is a spreadsheet doing another job, and its tab names are not ours to judge.
        if (!sheets.Any(s => SourceSheetNames.IsData(s.Name))) return findings;

        foreach (var sheet in sheets.Where(s => SourceSheetNames.IsData(s.Name)))
            CheckSheet(sheet, path, modName, modKey, findings);

        return findings;
    }

    private static void CheckSheet(XlsxSheet sheet, string path, string modName,
        string? modKey, List<HealthFinding> findings)
    {
        var last = sheet.LastContentRow;

        // Rows 1 to 3 are the header, the column types and the defaults, copied from the
        // official sheet. A sheet that stops above row 4 simply has no entries in it.
        if (last < SourceSheetNames.FirstDataRow) return;

        var header = sheet.Row(SourceSheetNames.HeaderRow);
        var idColumn = Array.FindIndex(header,
            h => string.Equals(h.Trim(), IdColumn, StringComparison.OrdinalIgnoreCase));

        if (idColumn < 0)
        {
            findings.Add(Finding(HealthSeverity.Broken, modName, modKey,
                $"{modName}: the sheet \"{sheet.Name}\" has no id column",
                "Every row is identified by its id, and row 1 of this sheet does not have "
                + "that column. Copy the first three rows of the official sheet in whole - "
                + "the header, the types and the defaults - and start your own data at "
                + $"row {SourceSheetNames.FirstDataRow}.",
                "Copy rows 1 to 3 from the official source sheet.",
                new[]
                {
                    $"{File(path)} · tab \"{sheet.Name}\"",
                    "row 1: " + (header.Length == 0 ? "(empty)" : string.Join(", ", header.Take(12))),
                }));

            return;
        }

        CheckRows(sheet, last, idColumn, path, modName, modKey, findings);
    }

    /// <summary>
    /// The one that costs people an evening: a blank id stops the sheet dead, and every
    /// row under it is dropped without a word.
    /// </summary>
    private static void CheckRows(XlsxSheet sheet, int last, int idColumn, string path,
        string modName, string? modKey, List<HealthFinding> findings)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        for (var row = SourceSheetNames.FirstDataRow; row <= last; row++)
        {
            var id = sheet.Cell(row, idColumn).Trim();

            if (id.Length == 0)
            {
                var below = 0;
                var examples = new List<string>();

                for (var after = row + 1; after <= last; after++)
                {
                    var other = sheet.Cell(after, idColumn).Trim();
                    if (other.Length == 0) continue;

                    below++;
                    if (examples.Count < 6) examples.Add($"row {after}: {other}");
                }

                // Trailing blank rows are harmless - there is nothing under them.
                if (below == 0) break;

                var evidence = new List<string>
                {
                    $"{File(path)} · tab \"{sheet.Name}\" · blank id at row {row}",
                };

                evidence.AddRange(examples);
                if (below > examples.Count) evidence.Add($"... and {below - examples.Count} more");

                findings.Add(Finding(HealthSeverity.Broken, modName, modKey,
                    $"{modName}: row {row} of \"{sheet.Name}\" is blank, and throws away {Rows(below)} under it",
                    $"A row with an empty id stops the sheet being read. The {Rows(below)} "
                    + $"below row {row} never reach the game, and nothing anywhere reports "
                    + "it - the mod loads and the content is simply missing.",
                    $"Delete row {row}, or give it an id. Blank rows cannot be used to "
                    + "group entries.",
                    evidence));

                return;
            }

            if (seen.TryGetValue(id, out var first))
            {
                if (duplicates.Count < 8) duplicates.Add($"{id} · rows {first} and {row}");
            }
            else
            {
                seen[id] = row;
            }
        }

        if (duplicates.Count > 0)
        {
            findings.Add(Finding(HealthSeverity.Conflict, modName, modKey,
                $"{modName}: \"{sheet.Name}\" defines the same id more than once",
                "Two rows share an id, so only one of them exists in the game. Which one "
                + "wins is decided by load order rather than by anything in the sheet.",
                "Give each row an id of its own.",
                duplicates.Prepend($"{File(path)} · tab \"{sheet.Name}\"")));
        }
    }

    private static string Rows(int count) => count == 1 ? "1 row" : $"{count} rows";

    /// <summary>
    /// The file, with the folder holding it. Mods routinely ship the same workbook under
    /// LangMod\EN and LangMod\CN, so the name on its own reads as the same problem
    /// reported twice when it is two files that each have it.
    /// </summary>
    private static string File(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);

        return folder.Length == 0 ? name : $"{folder}/{name}";
    }

    private static HealthFinding Finding(HealthSeverity severity, string modName,
        string? modKey, string title, string detail, string? suggestion,
        IEnumerable<string> evidence)
    {
        var finding = new HealthFinding
        {
            Severity = severity,
            Check = CheckName,
            Title = title,
            Detail = detail,
            Suggestion = suggestion,
        };

        finding.ModNames.Add(modName);
        if (!string.IsNullOrEmpty(modKey)) finding.ModKeys.Add(modKey);
        finding.Evidence.AddRange(evidence);

        return finding;
    }
}
