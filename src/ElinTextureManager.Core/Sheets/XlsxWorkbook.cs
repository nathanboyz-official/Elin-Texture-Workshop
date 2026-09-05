using System.IO.Compression;
using System.Xml.Linq;

namespace ElinTextureManager.Core.Sheets;

/// <summary>One tab of a workbook, as rows of cell text.</summary>
public sealed class XlsxSheet
{
    public required string Name { get; init; }

    /// <summary>
    /// Rows by their real spreadsheet number, so row 4 is row 4 even when the rows above
    /// it are absent from the file. Source sheets are read by row position - headers on
    /// 1, types on 2, defaults on 3, data from 4 - so a shifted row is a wrong answer.
    /// </summary>
    public Dictionary<int, string[]> Rows { get; } = new();

    public int LastRow => Rows.Count == 0 ? 0 : Rows.Keys.Max();

    /// <summary>
    /// The last row that actually holds something.
    ///
    /// Not the same as the last row: a spreadsheet carries formatting on rows that have
    /// no content, and several of the game's own files declare a row at 1,048,576 - the
    /// bottom of the sheet - which would otherwise be walked one row at a time.
    /// </summary>
    public int LastContentRow
    {
        get
        {
            var last = 0;

            foreach (var (number, cells) in Rows)
            {
                if (number > last && cells.Any(c => c.Trim().Length > 0)) last = number;
            }

            return last;
        }
    }

    public string[] Row(int number) => Rows.TryGetValue(number, out var r) ? r : Array.Empty<string>();

    /// <summary>The text in a cell, or empty. Never throws on a short row.</summary>
    public string Cell(int row, int column)
    {
        var cells = Row(row);
        return column >= 0 && column < cells.Length ? cells[column] : string.Empty;
    }
}

/// <summary>
/// Reads the parts of an .xlsx file this application needs, and nothing else.
///
/// An .xlsx is a zip of XML, so this needs no library: the alternative was a third-party
/// spreadsheet package, and this project deliberately carries no dependency anyone has to
/// trust for something the framework already does.
///
/// Only cell text is read. Formatting, formulas, merged cells and everything else are
/// ignored, because the game reads these files as a grid of strings and so does this.
/// </summary>
public static class XlsxWorkbook
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Rel =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Every tab in the workbook, in the order the file lists them. Throws only when the
    /// file is not a workbook at all; a damaged sheet inside comes back empty rather than
    /// taking the rest of the file down with it.
    /// </summary>
    /// <param name="maxRows">
    /// Stop after this many rows of each sheet, for callers that only want the header.
    ///
    /// Worth less than it looks: measured over 397 real workbooks it saved almost nothing,
    /// because the time goes on unzipping and parsing the XML rather than on building the
    /// rows. It earns its place only on a sheet with a great many rows - the game's own
    /// god_talk.xlsx declares one at 1,048,576 - where the arrays would otherwise be built
    /// and thrown away.
    ///
    /// Rows are written in order by every spreadsheet, so stopping early is safe. A file
    /// that somehow wrote them out of order would lose the stragglers, which is why the
    /// default reads everything.
    /// </param>
    public static List<XlsxSheet> Read(string path, int maxRows = int.MaxValue)
    {
        using var archive = ZipFile.OpenRead(path);

        var workbook = Entry(archive, "xl/workbook.xml")
            ?? throw new InvalidDataException("Not a workbook: xl/workbook.xml is missing.");

        var targets = ReadRelationships(archive);
        var shared = ReadSharedStrings(archive);

        var sheets = new List<XlsxSheet>();

        foreach (var element in workbook.Descendants(Main + "sheet"))
        {
            var name = (string?)element.Attribute("name") ?? string.Empty;
            var id = (string?)element.Attribute(Rel + "id");

            var sheet = new XlsxSheet { Name = name };
            sheets.Add(sheet);

            if (id is null || !targets.TryGetValue(id, out var target)) continue;

            var part = Entry(archive, target);
            if (part is null) continue;

            ReadRows(part, shared, sheet, maxRows);
        }

        return sheets;
    }

    private static void ReadRows(XDocument part, List<string> shared, XlsxSheet sheet,
        int maxRows)
    {
        var counted = 0;

        foreach (var row in part.Descendants(Main + "row"))
        {
            counted++;
            if (counted > maxRows) break;

            // The row's own number when it has one. Rows are allowed to be sparse, and a
            // count would silently move everything up.
            var number = (int?)row.Attribute("r") ?? counted;

            var cells = new List<string>();

            foreach (var cell in row.Elements(Main + "c"))
            {
                var column = ColumnOf((string?)cell.Attribute("r"));

                // Empty cells are simply absent from the file, so the gap has to be put
                // back or every column after one lands in the wrong place.
                if (column >= 0)
                {
                    while (cells.Count < column) cells.Add(string.Empty);
                }

                cells.Add(TextOf(cell, shared));
            }

            sheet.Rows[number] = cells.ToArray();
        }
    }

    private static string TextOf(XElement cell, List<string> shared)
    {
        var type = (string?)cell.Attribute("t");

        if (type == "s")
        {
            var raw = cell.Element(Main + "v")?.Value;
            return int.TryParse(raw, out var index) && index >= 0 && index < shared.Count
                ? shared[index]
                : string.Empty;
        }

        if (type == "inlineStr")
            return string.Concat(cell.Element(Main + "is")?.Descendants(Main + "t")
                .Select(t => t.Value) ?? Array.Empty<string>());

        return cell.Element(Main + "v")?.Value ?? string.Empty;
    }

    /// <summary>Zero-based column from a cell reference such as "AB12".</summary>
    private static int ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;

        var column = 0;
        var seen = false;

        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z') { column = (column * 26) + (c - 'A' + 1); seen = true; }
            else if (c is >= 'a' and <= 'z') { column = (column * 26) + (c - 'a' + 1); seen = true; }
            else break;
        }

        return seen ? column - 1 : -1;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var rels = Entry(archive, "xl/_rels/workbook.xml.rels");
        if (rels is null) return map;

        foreach (var r in rels.Descendants(PackageRel + "Relationship"))
        {
            var id = (string?)r.Attribute("Id");
            var target = (string?)r.Attribute("Target");
            if (id is null || target is null) continue;

            map[id] = target.StartsWith('/')
                ? target.TrimStart('/')
                : "xl/" + target.Replace("../", string.Empty);
        }

        return map;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var strings = new List<string>();
        var part = Entry(archive, "xl/sharedStrings.xml");
        if (part is null) return strings;

        foreach (var si in part.Descendants(Main + "si"))
        {
            // Rich text splits one string across several runs, which have to be joined
            // back or a coloured word turns into a different value.
            strings.Add(string.Concat(si.Descendants(Main + "t").Select(t => t.Value)));
        }

        return strings;
    }

    private static XDocument? Entry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

        if (entry is null) return null;

        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        catch
        {
            return null;
        }
    }
}
