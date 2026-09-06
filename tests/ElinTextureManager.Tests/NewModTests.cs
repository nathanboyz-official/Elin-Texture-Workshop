using System.Xml.Linq;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Sheets;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Writing .xlsx, which the framework can do without a library because the format is a
/// zip of XML.
/// </summary>
public sealed class XlsxWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("write").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void What_is_written_reads_back_the_same()
    {
        var file = Path("a.xlsx");

        XlsxWriter.Write(file, new[]
        {
            new XlsxOutSheet("Chara", new[]
            {
                new[] { "id", "name", "race" },
                new[] { "string", "string", "string" },
                new[] { "", "", "norland" },
                new[] { "agnes", "Agnes", "elea" },
            }),
        });

        var sheet = Assert.Single(XlsxWorkbook.Read(file));

        Assert.Equal("Chara", sheet.Name);
        Assert.Equal("id", sheet.Cell(1, 0));
        Assert.Equal("norland", sheet.Cell(3, 2));
        Assert.Equal("agnes", sheet.Cell(4, 0));
        Assert.Equal("elea", sheet.Cell(4, 2));
    }

    [Fact]
    public void An_empty_cell_survives_the_round_trip_in_the_right_place()
    {
        var file = Path("b.xlsx");

        XlsxWriter.Write(file, new[]
        {
            new XlsxOutSheet("Thing", new[]
            {
                new[] { "id", "name", "category" },
                new[] { "string", "string", "string" },
                new[] { "", "", "other" },
                new[] { "sword", "", "weapon" },
            }),
        });

        var sheet = Assert.Single(XlsxWorkbook.Read(file));

        Assert.Equal("sword", sheet.Cell(4, 0));
        Assert.Equal(string.Empty, sheet.Cell(4, 1));
        Assert.Equal("weapon", sheet.Cell(4, 2));
    }

    [Fact]
    public void Several_tabs_keep_their_names_and_order()
    {
        var file = Path("c.xlsx");
        var rows = new[] { new[] { "id" }, new[] { "string" }, new[] { "" } };

        XlsxWriter.Write(file, new[]
        {
            new XlsxOutSheet("Chara", rows),
            new XlsxOutSheet("Thing", rows),
            new XlsxOutSheet("Race", rows),
        });

        Assert.Equal(new[] { "Chara", "Thing", "Race" },
            XlsxWorkbook.Read(file).Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Characters_that_would_break_the_xml_are_carried_through()
    {
        var file = Path("d.xlsx");

        XlsxWriter.Write(file, new[]
        {
            new XlsxOutSheet("Chara", new[]
            {
                new[] { "id", "name" },
                new[] { "string", "string" },
                new[] { "", "" },
                new[] { "quote", "Bob \"the <Great>\" & Co" },
            }),
        });

        Assert.Equal("Bob \"the <Great>\" & Co",
            XlsxWorkbook.Read(file).Single().Cell(4, 1));
    }

    [Fact]
    public void A_workbook_this_writes_has_the_parts_a_reader_expects()
    {
        var file = Path("e.xlsx");
        XlsxWriter.Write(file, new[] { new XlsxOutSheet("Chara", new[] { new[] { "id" } }) });

        using var zip = System.IO.Compression.ZipFile.OpenRead(file);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // Without these two, Excel calls the file corrupt even though the sheets are fine.
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
    }

    [Fact]
    public void Column_letters_carry_past_z()
    {
        Assert.Equal("A", XlsxWriter.Reference(0));
        Assert.Equal("Z", XlsxWriter.Reference(25));
        Assert.Equal("AA", XlsxWriter.Reference(26));
        Assert.Equal("AZ", XlsxWriter.Reference(51));
        Assert.Equal("BA", XlsxWriter.Reference(52));
    }

    [Fact]
    public void A_wide_sheet_still_lands_in_the_right_columns()
    {
        // The Chara sheet is 49 columns, so this is not a hypothetical.
        var file = Path("wide.xlsx");
        var header = Enumerable.Range(0, 60).Select(i => i == 0 ? "id" : $"col{i}").ToArray();

        XlsxWriter.Write(file, new[] { new XlsxOutSheet("Chara", new[] { header }) });

        var sheet = XlsxWorkbook.Read(file).Single();

        Assert.Equal("col30", sheet.Cell(1, 30));
        Assert.Equal("col59", sheet.Cell(1, 59));
    }
}

/// <summary>
/// Working out the official first three rows from what the installed mods use, because
/// the official sheets are not shipped with the game.
/// </summary>
public sealed class SourceSheetTemplateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("templates").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Book(string name, string tab, string[] header)
    {
        var path = System.IO.Path.Combine(_dir, name);

        XlsxWriter.Write(path, new[]
        {
            new XlsxOutSheet(tab, new[]
            {
                header,
                header.Select(_ => "string").ToArray(),
                header.Select(h => h == "category" ? "other" : "").ToArray(),
                header.Select((_, i) => i == 0 ? "an_id" : "x").ToArray(),
            }),
        });

        return path;
    }

    [Fact]
    public void The_header_most_mods_agree_on_is_the_one_chosen()
    {
        var common = new[] { "id", "name", "category" };
        var odd = new[] { "id", "name" };

        var books = new[]
        {
            Book("a.xlsx", "Chara", common),
            Book("b.xlsx", "Chara", common),
            Book("c.xlsx", "Chara", common),
            Book("d.xlsx", "Chara", odd),
        };

        var template = Assert.Single(SourceSheetTemplates.Harvest(books));

        Assert.Equal("Chara", template.Tab);
        Assert.Equal(common, template.Header);
        Assert.Equal(3, template.Agreed);
        Assert.Equal(4, template.Seen);
        Assert.Equal(75, template.Percent);
    }

    [Fact]
    public void The_rows_beneath_the_header_come_with_it()
    {
        var books = new[] { Book("a.xlsx", "Thing", new[] { "id", "name", "category" }) };

        var template = Assert.Single(SourceSheetTemplates.Harvest(books));

        Assert.Equal(new[] { "string", "string", "string" }, template.Types);
        Assert.Equal(new[] { "", "", "other" }, template.Defaults);

        // Header, types, defaults - and no data, so the mod starts at row 4.
        Assert.Equal(3, template.Rows.Count);
    }

    [Fact]
    public void No_agreement_means_no_answer_offered()
    {
        var books = new[]
        {
            Book("a.xlsx", "Chara", new[] { "id", "one" }),
            Book("b.xlsx", "Chara", new[] { "id", "two" }),
            Book("c.xlsx", "Chara", new[] { "id", "three" }),
        };

        Assert.Empty(SourceSheetTemplates.Harvest(books));
    }

    [Fact]
    public void A_sheet_with_no_id_column_is_not_a_source_sheet()
    {
        var books = new[] { Book("a.xlsx", "Chara", new[] { "name", "category" }) };

        Assert.Empty(SourceSheetTemplates.Harvest(books));
    }

    [Fact]
    public void An_unreadable_file_does_not_stop_the_rest()
    {
        var bad = System.IO.Path.Combine(_dir, "bad.xlsx");
        File.WriteAllText(bad, "not a workbook");

        var books = new[] { bad, Book("a.xlsx", "Chara", new[] { "id", "name" }) };

        Assert.Single(SourceSheetTemplates.Harvest(books));
    }
}

/// <summary>Creating a mod folder the game will load.</summary>
public sealed class NewModTests : IDisposable
{
    private readonly TestWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private static NewModRequest Request(string id = "my_mod") => new()
    {
        Title = "My Mod",
        Id = id,
        Author = "Someone",
        Description = "A mod.",
        Tags = "Characters",
    };

    [Fact]
    public void A_new_mod_has_the_two_things_that_make_it_one()
    {
        var folder = NewMod.Create(_ws.Paths, Request());

        Assert.True(File.Exists(Path.Combine(folder, "package.xml")));
        Assert.EndsWith("my_mod", folder);
    }

    [Fact]
    public void The_package_xml_carries_the_fields_the_game_reads()
    {
        var folder = NewMod.Create(_ws.Paths, Request());
        var doc = XDocument.Load(Path.Combine(folder, "package.xml"));

        Assert.Equal("My Mod", doc.Root!.Element("title")!.Value);
        Assert.Equal("my_mod", doc.Root.Element("id")!.Value);
        Assert.Equal("Someone", doc.Root.Element("author")!.Value);
        Assert.Equal("false", doc.Root.Element("builtin")!.Value);
    }

    [Fact]
    public void A_title_with_a_quote_in_it_does_not_break_the_file()
    {
        var request = new NewModRequest
        {
            Title = "Bob's \"Big\" <Mod> & Friends",
            Id = "quoted",
            Author = "Bob",
        };

        var folder = NewMod.Create(_ws.Paths, request);
        var doc = XDocument.Load(Path.Combine(folder, "package.xml"));

        Assert.Equal("Bob's \"Big\" <Mod> & Friends", doc.Root!.Element("title")!.Value);
    }

    [Fact]
    public void The_sheets_asked_for_are_written_where_the_game_looks()
    {
        var request = Request();
        request.Sheets.Add(new SheetTemplate("Chara",
            new[] { "id", "name" }, new[] { "string", "string" }, new[] { "", "" }, 9, 10));

        var folder = NewMod.Create(_ws.Paths, request);
        var book = Path.Combine(folder, "LangMod", "EN", "Source.xlsx");

        Assert.True(File.Exists(book));

        var sheet = Assert.Single(XlsxWorkbook.Read(book));

        Assert.Equal("Chara", sheet.Name);
        Assert.Equal("id", sheet.Cell(1, 0));
        Assert.Equal("string", sheet.Cell(2, 0));

        // Nothing on row 4: that is where the author's own first entry goes.
        Assert.Equal(string.Empty, sheet.Cell(SourceSheetNames.FirstDataRow, 0));
        Assert.True(sheet.LastContentRow < SourceSheetNames.FirstDataRow);
    }

    [Fact]
    public void A_mod_this_makes_passes_the_check_that_reads_it_back()
    {
        var request = Request();
        request.Sheets.Add(new SheetTemplate("Chara",
            new[] { "id", "name" }, new[] { "string", "string" }, new[] { "", "" }, 9, 10));

        var folder = NewMod.Create(_ws.Paths, request);
        var book = Path.Combine(folder, "LangMod", "EN", "Source.xlsx");

        Assert.Empty(SourceSheetChecker.Check(book, "My Mod"));
    }

    [Fact]
    public void The_folders_asked_for_are_created()
    {
        var request = Request();
        request.Folders.Add("Portrait");
        request.Folders.Add(Path.Combine("Actor", "PCC", "female"));

        var folder = NewMod.Create(_ws.Paths, request);

        Assert.True(Directory.Exists(Path.Combine(folder, "Portrait")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Actor", "PCC", "female")));
    }

    [Fact]
    public void An_existing_folder_is_never_written_into()
    {
        NewMod.Create(_ws.Paths, Request());

        var again = Assert.Throws<InvalidOperationException>(
            () => NewMod.Create(_ws.Paths, Request()));

        Assert.Contains("already a folder", again.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("bad/slash")]
    public void An_id_that_cannot_be_a_folder_name_is_refused(string id)
    {
        Assert.False(NewMod.CheckId(id).Ok);
    }

    [Fact]
    public void A_sensible_id_is_accepted()
    {
        Assert.True(NewMod.CheckId("my_mod_2").Ok);
    }
}

/// <summary>The row cap, which exists for sheets with a great many rows.</summary>
public sealed class XlsxRowCapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cap").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Reading_capped_at_three_rows_gives_the_header_rows_and_no_more()
    {
        var path = Path.Combine(_dir, "big.xlsx");

        var rows = new List<string[]>
        {
            new[] { "id", "name" },
            new[] { "string", "string" },
            new[] { "", "" },
        };

        for (var i = 0; i < 500; i++) rows.Add(new[] { "row" + i, "Name " + i });

        XlsxWriter.Write(path, new[] { new XlsxOutSheet("Chara", rows) });

        var capped = XlsxWorkbook.Read(path, 3).Single();

        Assert.Equal("id", capped.Cell(1, 0));
        Assert.Equal("", capped.Cell(4, 0));
        Assert.Equal(3, capped.LastRow);

        // And the uncapped read still sees everything.
        Assert.Equal(503, XlsxWorkbook.Read(path).Single().LastRow);
    }
}
