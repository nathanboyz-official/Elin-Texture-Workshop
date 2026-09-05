using System.IO.Compression;
using System.Text;
using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.Sheets;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Builds real .xlsx files so the reader is tested against the format rather than
/// against a stand-in for it.
/// </summary>
internal static class Xlsx
{
    /// <summary>
    /// Writes a workbook of the given tabs. Each tab is rows of cells; a null row is a
    /// row absent from the file entirely, which is how a spreadsheet stores a gap.
    /// </summary>
    public static string Write(string path, params (string Name, string?[][] Rows)[] sheets)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var sheetXml = string.Join("", sheets.Select((s, i) =>
            $"<sheet name=\"{Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"));

        Add(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + $"<sheets>{sheetXml}</sheets></workbook>");

        var rels = string.Join("", sheets.Select((_, i) =>
            $"<Relationship Id=\"rId{i + 1}\" Target=\"worksheets/sheet{i + 1}.xml\" "
            + "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"/>"));

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + rels + "</Relationships>");

        for (var i = 0; i < sheets.Length; i++)
        {
            var rows = new StringBuilder();

            for (var r = 0; r < sheets[i].Rows.Length; r++)
            {
                var cells = sheets[i].Rows[r];
                if (cells is null) continue;

                rows.Append($"<row r=\"{r + 1}\">");

                for (var c = 0; c < cells.Length; c++)
                {
                    // An empty cell is left out, exactly as Excel leaves it out.
                    if (cells[c] is null) continue;

                    rows.Append($"<c r=\"{Reference(c)}{r + 1}\" t=\"inlineStr\">"
                                + $"<is><t>{Escape(cells[c]!)}</t></is></c>");
                }

                rows.Append("</row>");
            }

            Add(zip, $"xl/worksheets/sheet{i + 1}.xml",
                "<?xml version=\"1.0\"?>"
                + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + $"<sheetData>{rows}</sheetData></worksheet>");
        }

        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string Reference(int column)
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

    private static string Escape(string v) =>
        v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>The three rows every source sheet copies from the official one.</summary>
    public static string?[][] WithHeader(params string?[][] dataRows)
    {
        var rows = new List<string?[]>
        {
            new[] { "id", "name", "category" },
            new[] { "string", "string", "string" },
            new[] { "", "", "other" },
        };

        rows.AddRange(dataRows);
        return rows.ToArray();
    }
}

/// <summary>
/// Reading .xlsx without a spreadsheet library, because the format is a zip of XML and
/// this project carries no dependency for something the framework already does.
/// </summary>
public sealed class XlsxReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xlsx").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void Tabs_come_back_with_their_names()
    {
        var file = Xlsx.Write(Path("a.xlsx"),
            ("Chara", Xlsx.WithHeader()),
            ("Thing", Xlsx.WithHeader()));

        var sheets = XlsxWorkbook.Read(file);

        Assert.Equal(new[] { "Chara", "Thing" }, sheets.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Cells_come_back_as_text()
    {
        var file = Xlsx.Write(Path("b.xlsx"),
            ("Thing", Xlsx.WithHeader(new[] { "my_sword", "My Sword", "weapon" })));

        var sheet = XlsxWorkbook.Read(file).Single();

        Assert.Equal("id", sheet.Cell(1, 0));
        Assert.Equal("my_sword", sheet.Cell(4, 0));
        Assert.Equal("My Sword", sheet.Cell(4, 1));
    }

    [Fact]
    public void An_empty_cell_does_not_shift_the_ones_after_it()
    {
        // Excel leaves empty cells out of the file entirely. Read naively, "weapon" would
        // land in the name column and every check after would be judging the wrong value.
        var file = Xlsx.Write(Path("c.xlsx"),
            ("Thing", Xlsx.WithHeader(new string?[] { "my_sword", null, "weapon" })));

        var sheet = XlsxWorkbook.Read(file).Single();

        Assert.Equal("my_sword", sheet.Cell(4, 0));
        Assert.Equal(string.Empty, sheet.Cell(4, 1));
        Assert.Equal("weapon", sheet.Cell(4, 2));
    }

    [Fact]
    public void Rows_keep_their_real_numbers_when_one_is_missing()
    {
        // Row 5 is absent. Row 6 must still be row 6, or a reported line number sends
        // someone to the wrong row of their spreadsheet.
        var file = Xlsx.Write(Path("d.xlsx"),
            ("Thing", Xlsx.WithHeader(
                new[] { "one", "One", "x" },
                null!,
                new[] { "three", "Three", "x" })));

        var sheet = XlsxWorkbook.Read(file).Single();

        Assert.Equal("one", sheet.Cell(4, 0));
        Assert.Equal(string.Empty, sheet.Cell(5, 0));
        Assert.Equal("three", sheet.Cell(6, 0));
    }

    [Fact]
    public void Reading_something_that_is_not_a_workbook_says_so()
    {
        var file = Path("not.xlsx");
        File.WriteAllText(file, "this is not a zip");

        Assert.ThrowsAny<Exception>(() => XlsxWorkbook.Read(file));
    }
}

/// <summary>
/// The mistakes Elin will not tell you about.
///
/// Every rule here is one the modding wiki documents as a silent failure: the mod loads,
/// the game starts, and the content is simply not there.
/// </summary>
public sealed class SourceSheetCheckerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sheets").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    private List<HealthFinding> Check(string?[][] rows, string tab = "Thing") =>
        SourceSheetChecker.Check(Xlsx.Write(Path("s.xlsx"), (tab, rows)), "Test Mod", "test");

    [Fact]
    public void A_blank_id_row_is_reported_with_what_it_costs()
    {
        // The documented killer: a row with no id stops the sheet, and every row under it
        // is dropped with no warning anywhere.
        var findings = Check(Xlsx.WithHeader(
            new[] { "sword", "Sword", "weapon" },
            new string?[] { null, null, null },
            new[] { "shield", "Shield", "armour" },
            new[] { "helm", "Helm", "armour" }));

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("row 5", finding.Title);
        Assert.Contains("2 rows", finding.Title);
        Assert.Contains("shield", string.Join(" ", finding.Evidence));
    }

    [Fact]
    public void Trailing_blank_rows_are_not_a_problem()
    {
        // There is nothing under them to throw away.
        var findings = Check(Xlsx.WithHeader(
            new[] { "sword", "Sword", "weapon" },
            new string?[] { null, null, null }));

        Assert.Empty(findings);
    }

    [Fact]
    public void A_clean_sheet_reports_nothing()
    {
        var findings = Check(Xlsx.WithHeader(
            new[] { "sword", "Sword", "weapon" },
            new[] { "shield", "Shield", "armour" }));

        Assert.Empty(findings);
    }

    [Fact]
    public void A_workbook_with_no_source_tabs_is_left_entirely_alone()
    {
        // Mods carry plenty of other spreadsheets - dialog.xlsx, Name.xlsx, Alias.xlsx,
        // chara_talk.xlsx - whose tabs are named by their own conventions. Judging those
        // produced 270 findings against a real library, every one of them wrong.
        var findings = SourceSheetChecker.Check(
            Xlsx.Write(Path("dialog.xlsx"),
                ("general", Xlsx.WithHeader(new[] { "a", "b", "c" })),
                ("rumor", Xlsx.WithHeader(new[] { "d", "e", "f" })),
                ("1", Xlsx.WithHeader(new[] { "g", "h", "i" }))),
            "Test Mod");

        Assert.Empty(findings);
    }

    [Fact]
    public void An_unknown_tab_beside_a_real_one_is_not_reported_either()
    {
        // "Alias_EN" sits next to real source tabs in the base game's own files.
        var findings = SourceSheetChecker.Check(
            Xlsx.Write(Path("mixed.xlsx"),
                ("Thing", Xlsx.WithHeader(new[] { "sword", "Sword", "weapon" })),
                ("Alias_EN", Xlsx.WithHeader(new[] { "x", "y", "z" }))),
            "Test Mod");

        Assert.Empty(findings);
    }

    [Fact]
    public void An_empty_default_named_tab_is_left_alone()
    {
        // Every new workbook has one, and it is not a mistake.
        var findings = SourceSheetChecker.Check(
            Xlsx.Write(Path("t.xlsx"), ("Sheet1", new string?[][] { })), "Test Mod");

        Assert.Empty(findings);
    }

    [Fact]
    public void A_sheet_with_no_id_column_is_reported()
    {
        var rows = new string?[][]
        {
            new[] { "name", "category" },
            new[] { "string", "string" },
            new[] { "", "other" },
            new[] { "Sword", "weapon" },
        };

        var finding = Assert.Single(Check(rows));

        Assert.Equal(HealthSeverity.Broken, finding.Severity);
        Assert.Contains("no id column", finding.Title);
    }

    [Fact]
    public void The_same_id_twice_is_reported_as_a_conflict()
    {
        var findings = Check(Xlsx.WithHeader(
            new[] { "sword", "Sword", "weapon" },
            new[] { "sword", "Sword Again", "weapon" }));

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Conflict, finding.Severity);
        Assert.Contains("rows 4 and 5", string.Join(" ", finding.Evidence));
    }

    [Fact]
    public void A_sheet_with_only_the_three_header_rows_says_nothing()
    {
        // Headers and no entries yet is a sheet someone is part way through, not a fault.
        var rows = new string?[][]
        {
            new[] { "id", "name" },
            new[] { "string", "string" },
            new[] { "", "" },
        };

        Assert.Empty(Check(rows));
    }

    [Fact]
    public void Formatting_far_below_the_data_does_not_make_a_sheet_enormous()
    {
        // The game's own god_talk.xlsx declares a row at the bottom of the sheet. Walking
        // to it one row at a time takes a million steps and finds nothing.
        var rows = new List<string?[]>(Xlsx.WithHeader(new[] { "sword", "Sword", "weapon" }));
        while (rows.Count < 60000) rows.Add(null!);
        rows.Add(new string?[] { "", "", "" });

        var sheet = XlsxWorkbook.Read(
            Xlsx.Write(Path("big.xlsx"), ("Thing", rows.ToArray()))).Single();

        Assert.Equal(60001, sheet.LastRow);
        Assert.Equal(4, sheet.LastContentRow);
        Assert.Empty(SourceSheetChecker.Check(Path("big.xlsx"), "Test Mod"));
    }

    [Fact]
    public void A_lang_sheet_is_not_judged_by_the_data_rules()
    {
        // Lang sheets have no id column and are none the worse for it.
        var rows = new string?[][]
        {
            new[] { "key", "text" },
            new[] { "string", "string" },
            new[] { "", "" },
            new[] { "greeting", "Hello" },
        };

        Assert.Empty(Check(rows, tab: "General"));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_passed_over_rather_than_crashing()
    {
        // Something with an .xlsx name that is not a workbook is not this check's
        // business to complain about - it may not even be a source sheet.
        var file = Path("broken.xlsx");
        File.WriteAllText(file, "not a workbook");

        Assert.Empty(SourceSheetChecker.Check(file, "Test Mod"));
    }

    [Fact]
    public void Every_supported_tab_name_is_accepted()
    {
        foreach (var name in SourceSheetNames.Data.Concat(SourceSheetNames.Lang))
            Assert.True(SourceSheetNames.IsKnown(name), name);
    }
}
