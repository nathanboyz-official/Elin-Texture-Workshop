using System.IO.Compression;
using System.Text;

namespace ElinTextureManager.Core.Sheets;

/// <summary>A tab to write: a name and its rows, top to bottom, left to right.</summary>
public sealed record XlsxOutSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Writes a workbook Excel, LibreOffice and the game will all open.
///
/// The counterpart of <see cref="XlsxWorkbook"/>, and like it, no library: an .xlsx is a
/// zip of XML and the framework has both.
///
/// Text is written inline rather than through a shared-string table. A shared table is
/// how Excel saves - it stores each distinct string once and points at it - but it is an
/// optimisation, and writing one correctly means keeping a second index in step with
/// every cell. Inline is the same file to every reader and has nothing to keep in step.
/// These sheets are a few hundred rows; the size difference does not matter.
/// </summary>
public static class XlsxWriter
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Content = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static void Write(string path, IReadOnlyList<XlsxOutSheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("A workbook needs at least one sheet.");

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // Written beside the target and moved into place, so an interrupted write cannot
        // leave a half-zipped file where a workbook should be.
        var temp = path + ".tmp";

        using (var file = File.Create(temp))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
            Add(zip, "_rels/.rels", RootRels());
            Add(zip, "xl/workbook.xml", Workbook(sheets));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));

            for (var i = 0; i < sheets.Count; i++)
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(sheets[i]));
        }

        File.Move(temp, path, overwrite: true);
    }

    private static string ContentTypes(int count)
    {
        var overrides = new StringBuilder();

        for (var i = 1; i <= count; i++)
        {
            overrides.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" "
                             + "ContentType=\"application/vnd.openxmlformats-officedocument"
                             + ".spreadsheetml.worksheet+xml\"/>");
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + $"<Types xmlns=\"{Content}\">"
               + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
               + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
               + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
               + overrides
               + "</Types>";
    }

    private static string RootRels() =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<Relationships xmlns=\"{PackageRel}\">"
        + $"<Relationship Id=\"rId1\" Type=\"{Rel}/officeDocument\" Target=\"xl/workbook.xml\"/>"
        + "</Relationships>";

    private static string Workbook(IReadOnlyList<XlsxOutSheet> sheets)
    {
        var tabs = new StringBuilder();

        for (var i = 0; i < sheets.Count; i++)
        {
            tabs.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" "
                        + $"r:id=\"rId{i + 1}\"/>");
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>{tabs}</sheets></workbook>";
    }

    private static string WorkbookRels(int count)
    {
        var rels = new StringBuilder();

        for (var i = 1; i <= count; i++)
        {
            rels.Append($"<Relationship Id=\"rId{i}\" Type=\"{Rel}/worksheet\" "
                        + $"Target=\"worksheets/sheet{i}.xml\"/>");
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + $"<Relationships xmlns=\"{PackageRel}\">{rels}</Relationships>";
    }

    private static string Sheet(XlsxOutSheet sheet)
    {
        var rows = new StringBuilder();

        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var cells = sheet.Rows[r];
            rows.Append($"<row r=\"{r + 1}\">");

            for (var c = 0; c < cells.Count; c++)
            {
                // An empty cell is left out entirely, which is what a spreadsheet does and
                // what the reader on the other side expects.
                if (string.IsNullOrEmpty(cells[c])) continue;

                rows.Append($"<c r=\"{Reference(c)}{r + 1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">"
                            + Escape(cells[c]) + "</t></is></c>");
            }

            rows.Append("</row>");
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
               + $"<worksheet xmlns=\"{Main}\"><sheetData>{rows}</sheetData></worksheet>";
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Column letters for a zero-based index: 0 is A, 26 is AA.</summary>
    public static string Reference(int column)
    {
        var name = string.Empty;
        column++;

        while (column > 0)
        {
            var rem = (column - 1) % 26;
            name = (char)('A' + rem) + name;
            column = (column - 1) / 26;
        }

        return name;
    }

    private static string Escape(string value)
    {
        var text = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            // XML 1.0 cannot carry most control characters at all, and a stray one makes
            // the whole workbook unreadable rather than one cell wrong.
            if (c < 0x20 && c is not ('\t' or '\n' or '\r')) continue;

            text.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => c.ToString(),
            });
        }

        return text.ToString();
    }
}
