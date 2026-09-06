using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Sheets;

/// <summary>The three rows a source sheet starts with, and how sure we are of them.</summary>
public sealed record SheetTemplate(
    string Tab,
    IReadOnlyList<string> Header,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Defaults,
    int Agreed,
    int Seen)
{
    /// <summary>How many of the sheets seen had exactly this header.</summary>
    public int Percent => Seen == 0 ? 0 : Agreed * 100 / Seen;

    public IReadOnlyList<IReadOnlyList<string>> Rows => new[] { Header, Types, Defaults };
}

/// <summary>
/// Works out what the official first three rows of each source sheet look like, by
/// looking at what the installed mods use.
///
/// The official sheets live in Google Sheets and are not shipped with the game, so there
/// is nothing on the machine to copy them from - except the mods, every one of which was
/// told to copy those three rows in whole. Take the header that the most of them agree
/// on and that is the official one.
///
/// It is not a guess. Across one real library, 76 of 96 Chara sheets carry the same 49
/// columns and 32 of 45 Thing sheets the same 52 - and that Thing header has the column
/// "sort" in it twice, which is the exact oddity the modding wiki warns about in the
/// official sheet. Agreement that reproduces a known quirk is agreement about the real
/// thing.
/// </summary>
public static class SourceSheetTemplates
{
    /// <summary>Below this much agreement, there is no answer worth offering.</summary>
    public const int MinimumPercent = 50;

    /// <summary>
    /// Reads every workbook given and returns one template per tab, best first. Files
    /// that cannot be read are skipped rather than failing the lot.
    /// </summary>
    public static List<SheetTemplate> Harvest(IEnumerable<string> workbooks)
    {
        // tab -> header row (joined) -> how many files had it, and the rows beneath it
        var votes = new Dictionary<string, Dictionary<string, Vote>>(StringComparer.Ordinal);

        foreach (var path in workbooks)
        {
            List<XlsxSheet> sheets;

            try { sheets = XlsxWorkbook.Read(path, SourceSheetNames.DefaultRow); }
            catch { continue; }

            foreach (var sheet in sheets.Where(s => SourceSheetNames.IsData(s.Name)))
            {
                var header = sheet.Row(SourceSheetNames.HeaderRow);
                if (header.Length == 0 || header.All(h => h.Trim().Length == 0)) continue;

                // A sheet has to have an id column to be one of these at all.
                if (!header.Any(h => string.Equals(h.Trim(), SourceSheetChecker.IdColumn,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var key = string.Join("", header);

                if (!votes.TryGetValue(sheet.Name, out var forTab))
                    votes[sheet.Name] = forTab = new Dictionary<string, Vote>(StringComparer.Ordinal);

                if (forTab.TryGetValue(key, out var vote)) vote.Count++;
                else
                {
                    forTab[key] = new Vote
                    {
                        Count = 1,
                        Header = header,
                        Types = sheet.Row(SourceSheetNames.TypeRow),
                        Defaults = sheet.Row(SourceSheetNames.DefaultRow),
                    };
                }
            }
        }

        var templates = new List<SheetTemplate>();

        foreach (var (tab, forTab) in votes)
        {
            var seen = forTab.Values.Sum(v => v.Count);
            var best = forTab.Values.OrderByDescending(v => v.Count).First();

            if (best.Count * 100 / seen < MinimumPercent) continue;

            templates.Add(new SheetTemplate(
                tab,
                best.Header,
                Padded(best.Types, best.Header.Length),
                Padded(best.Defaults, best.Header.Length),
                best.Count,
                seen));
        }

        AppLog.Info($"Source sheet templates: {templates.Count} tabs derived from installed mods.");

        return templates
            .OrderByDescending(t => t.Seen)
            .ThenBy(t => t.Tab, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The types and defaults rows can be shorter than the header when their last cells
    /// are empty, and a short row would leave the sheet ragged.
    /// </summary>
    private static IReadOnlyList<string> Padded(IReadOnlyList<string> row, int width)
    {
        if (row.Count >= width) return row.Take(width).ToList();

        var padded = row.ToList();
        while (padded.Count < width) padded.Add(string.Empty);

        return padded;
    }

    private sealed class Vote
    {
        public int Count;
        public required string[] Header { get; init; }
        public required string[] Types { get; init; }
        public required string[] Defaults { get; init; }
    }
}
