using ElinTextureManager.Core.Sheets;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Opening a source sheet, changing it, and writing it back.
///
/// The thing that must never happen is losing something by having opened the file. These
/// are other people's mods, and a sheet that comes back missing a tab, a column or the
/// rows the game reads first is worse than one nobody could edit at all.
/// </summary>
public sealed class SourceSheetDocumentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("edit").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    private string Sample(string name = "s.xlsx")
    {
        var path = Path(name);

        XlsxWriter.Write(path, new[]
        {
            new XlsxOutSheet("Chara", new[]
            {
                new[] { "id", "name", "race" },
                new[] { "string", "string", "string" },
                new[] { "", "", "norland" },
                new[] { "agnes", "Agnes", "elea" },
                new[] { "bob", "Bob", "" },
            }),
            new XlsxOutSheet("Notes", new[]
            {
                new[] { "anything", "at all" },
                new[] { "second", "row" },
            }),
        });

        return path;
    }

    [Fact]
    public void Only_the_tabs_the_game_reads_are_opened_for_editing()
    {
        var document = SourceSheetDocument.Load(Sample());

        var tab = Assert.Single(document.Tabs);
        Assert.Equal("Chara", tab.Name);
    }

    [Fact]
    public void The_three_rows_the_game_reads_first_are_kept_apart_from_the_entries()
    {
        var tab = SourceSheetDocument.Load(Sample()).Tabs.Single();

        Assert.Equal(new[] { "id", "name", "race" }, tab.Header);
        Assert.Equal(new[] { "string", "string", "string" }, tab.Types);
        Assert.Equal(new[] { "", "", "norland" }, tab.Defaults);

        Assert.Equal(2, tab.Entries.Count);
        Assert.Equal("agnes", tab.Entries[0][0]);
        Assert.Equal("bob", tab.Entries[1][0]);
    }

    [Fact]
    public void Saving_without_changing_anything_leaves_the_sheet_as_it_was()
    {
        var path = Sample();
        SourceSheetDocument.Load(path).Save();

        var sheet = XlsxWorkbook.Read(path).First();

        Assert.Equal("id", sheet.Cell(1, 0));
        Assert.Equal("string", sheet.Cell(2, 0));
        Assert.Equal("norland", sheet.Cell(3, 2));
        Assert.Equal("agnes", sheet.Cell(4, 0));
        Assert.Equal("bob", sheet.Cell(5, 0));
    }

    [Fact]
    public void A_tab_the_editor_does_not_understand_survives_the_round_trip()
    {
        // A mod's workbook may hold notes or a working tab. Dropping it would be a data
        // loss the author only discovers later.
        var path = Sample();
        SourceSheetDocument.Load(path).Save();

        var notes = XlsxWorkbook.Read(path).Single(s => s.Name == "Notes");

        Assert.Equal("anything", notes.Cell(1, 0));
        Assert.Equal("at all", notes.Cell(1, 1));
        Assert.Equal("second", notes.Cell(2, 0));
    }

    [Fact]
    public void An_added_entry_lands_on_the_next_row()
    {
        var path = Sample();
        var document = SourceSheetDocument.Load(path);
        var tab = document.Tabs.Single();

        var entry = tab.NewEntry();
        entry[0] = "carol";
        entry[1] = "Carol";
        tab.Entries.Add(entry);

        document.Save();

        var sheet = XlsxWorkbook.Read(path).First();

        Assert.Equal("carol", sheet.Cell(6, 0));
        Assert.Equal("Carol", sheet.Cell(6, 1));
    }

    [Fact]
    public void A_deleted_entry_is_gone_and_the_rest_move_up()
    {
        var path = Sample();
        var document = SourceSheetDocument.Load(path);
        var tab = document.Tabs.Single();

        tab.Entries.RemoveAt(0);
        document.Save();

        var sheet = XlsxWorkbook.Read(path).First();

        Assert.Equal("bob", sheet.Cell(4, 0));
        Assert.Equal(string.Empty, sheet.Cell(5, 0));
    }

    [Fact]
    public void A_short_row_is_padded_so_the_grid_is_never_ragged()
    {
        var path = Path("short.xlsx");

        XlsxWriter.Write(path, new[]
        {
            new XlsxOutSheet("Chara", new[]
            {
                new[] { "id", "name", "race" },
                new[] { "string", "string", "string" },
                new[] { "", "", "" },
                new[] { "agnes" },
            }),
        });

        var tab = SourceSheetDocument.Load(path).Tabs.Single();

        Assert.Equal(3, tab.Entries[0].Length);
        Assert.Equal("agnes", tab.Entries[0][0]);
        Assert.Equal(string.Empty, tab.Entries[0][2]);
    }

    [Fact]
    public void The_id_column_is_found_wherever_it_sits()
    {
        var path = Path("moved.xlsx");

        XlsxWriter.Write(path, new[]
        {
            new XlsxOutSheet("Thing", new[]
            {
                new[] { "name", "id", "category" },
                new[] { "string", "string", "string" },
                new[] { "", "", "other" },
                new[] { "Sword", "sword", "weapon" },
            }),
        });

        Assert.Equal(1, SourceSheetDocument.Load(path).Tabs.Single().IdColumn);
    }

    [Fact]
    public void A_sheet_edited_here_still_passes_the_checker()
    {
        var path = Sample();
        var document = SourceSheetDocument.Load(path);
        var tab = document.Tabs.Single();

        var entry = tab.NewEntry();
        entry[0] = "carol";
        tab.Entries.Add(entry);
        document.Save();

        Assert.Empty(SourceSheetChecker.Check(path, "Test Mod"));
    }

    [Fact]
    public void A_blank_row_kept_in_the_middle_is_still_reported_by_the_checker()
    {
        // The editor does not silently remove them, so the check has to still see it.
        var path = Sample();
        var document = SourceSheetDocument.Load(path);
        var tab = document.Tabs.Single();

        tab.Entries.Insert(1, tab.NewEntry());
        document.Save();

        var finding = Assert.Single(SourceSheetChecker.Check(path, "Test Mod"));

        Assert.Contains("throws away", finding.Title);
    }
}

/// <summary>Keeping a copy before writing over somebody's mod.</summary>
public sealed class FileBackupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("backup").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void A_copy_is_taken_and_holds_what_the_file_held()
    {
        var path = System.IO.Path.Combine(_dir, "sheet.xlsx");
        File.WriteAllText(path, "before");

        var copy = FileBackup.Take(path);

        Assert.NotNull(copy);
        Assert.Equal("before", File.ReadAllText(copy!));

        File.Delete(copy!);
    }

    [Fact]
    public void Backing_up_a_file_that_is_not_there_is_not_an_error()
    {
        Assert.Null(FileBackup.Take(System.IO.Path.Combine(_dir, "missing.xlsx")));
    }
}
