using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Sheets;

/// <summary>One editable tab: the three rows the game reads first, then the entries.</summary>
public sealed class SourceSheetTab
{
    public required string Name { get; init; }

    /// <summary>Row 1. Never edited here - the game matches columns by these names.</summary>
    public required IReadOnlyList<string> Header { get; init; }

    /// <summary>Row 2, the column types.</summary>
    public required IReadOnlyList<string> Types { get; init; }

    /// <summary>Row 3, what an empty cell falls back to.</summary>
    public required IReadOnlyList<string> Defaults { get; init; }

    /// <summary>Row 4 down. Every row is exactly <see cref="Header"/> wide.</summary>
    public List<string[]> Entries { get; } = new();

    public int IdColumn => Array.FindIndex(Header.ToArray(),
        h => string.Equals(h.Trim(), SourceSheetChecker.IdColumn, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The tab's name. Named here rather than left to a dropdown's DisplayMemberPath,
    /// because the dark theme's combo box ignores that and falls back to ToString - which
    /// on a class is the type name.
    /// </summary>
    public override string ToString() => Name;

    public string[] NewEntry()
    {
        var row = new string[Header.Count];

        for (var i = 0; i < row.Length; i++) row[i] = string.Empty;

        return row;
    }
}

/// <summary>
/// A source sheet workbook, opened so it can be edited and written back.
///
/// The first three rows are carried through untouched. They are the header, the column
/// types and the defaults, and the game reads its columns out of them - an editor that
/// rewrote them from its own idea of the schema would quietly change what every empty
/// cell in the file means.
///
/// Tabs the game does not read as source data are carried through as well, cell for cell.
/// A mod's workbook may hold notes, working columns or a translation tab, and none of
/// that is this editor's to discard.
/// </summary>
public sealed class SourceSheetDocument
{
    private readonly List<XlsxSheet> _passthrough = new();

    private SourceSheetDocument(string path) => Path = path;

    public string Path { get; }

    /// <summary>The tabs that can be edited, in the order the file lists them.</summary>
    public List<SourceSheetTab> Tabs { get; } = new();

    public static SourceSheetDocument Load(string path)
    {
        var document = new SourceSheetDocument(path);

        foreach (var sheet in XlsxWorkbook.Read(path))
        {
            if (!SourceSheetNames.IsData(sheet.Name))
            {
                document._passthrough.Add(sheet);
                continue;
            }

            var header = sheet.Row(SourceSheetNames.HeaderRow);

            // Without a header there is nothing to edit against, so it is carried through
            // rather than opened.
            if (header.Length == 0 || header.All(h => h.Trim().Length == 0))
            {
                document._passthrough.Add(sheet);
                continue;
            }

            var tab = new SourceSheetTab
            {
                Name = sheet.Name,
                Header = header,
                Types = Fit(sheet.Row(SourceSheetNames.TypeRow), header.Length),
                Defaults = Fit(sheet.Row(SourceSheetNames.DefaultRow), header.Length),
            };

            var last = sheet.LastContentRow;

            for (var row = SourceSheetNames.FirstDataRow; row <= last; row++)
            {
                var cells = Fit(sheet.Row(row), header.Length);

                // A row that is entirely empty is a gap the author left. It is kept,
                // because deleting it silently would change which rows the game reads.
                tab.Entries.Add(cells);
            }

            document.Tabs.Add(tab);
        }

        return document;
    }

    /// <summary>
    /// Writes the workbook back. Every tab is written, edited or not, so nothing in the
    /// file is lost by having opened it.
    /// </summary>
    public void Save()
    {
        var sheets = new List<XlsxOutSheet>();

        foreach (var tab in Tabs)
        {
            var rows = new List<IReadOnlyList<string>>
            {
                tab.Header,
                tab.Types,
                tab.Defaults,
            };

            rows.AddRange(tab.Entries);
            sheets.Add(new XlsxOutSheet(tab.Name, rows));
        }

        foreach (var sheet in _passthrough)
        {
            var rows = new List<IReadOnlyList<string>>();
            var last = sheet.LastRow;

            for (var row = 1; row <= last; row++) rows.Add(sheet.Row(row));

            sheets.Add(new XlsxOutSheet(sheet.Name, rows));
        }

        XlsxWriter.Write(Path, sheets);
        AppLog.Info($"Saved source sheet {Path} ({Tabs.Count} editable tabs).");
    }

    /// <summary>Rows shorter than the header are padded so the grid is never ragged.</summary>
    private static string[] Fit(string[] row, int width)
    {
        if (row.Length == width) return row;

        var fitted = new string[width];

        for (var i = 0; i < width; i++) fitted[i] = i < row.Length ? row[i] : string.Empty;

        return fitted;
    }
}
