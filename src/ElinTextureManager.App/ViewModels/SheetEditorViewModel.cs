using System.Data;
using System.IO;
using System.Windows;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Sheets;

namespace ElinTextureManager.App.ViewModels;

/// <summary>
/// Editing one source sheet.
///
/// The grid holds only the entries. The three rows above them - the header, the column
/// types and the defaults - are shown but not editable: the game reads its columns out of
/// them, and an editor that let them drift would change what every empty cell in the file
/// means without saying so.
///
/// The column a cell falls back to when left empty is shown in its header, because that
/// is the single most surprising thing about these sheets. A blank category is not blank;
/// it is "other".
/// </summary>
public sealed class SheetEditorViewModel : ObservableObject
{
    private readonly SourceSheetDocument _document;

    public SheetEditorViewModel(SourceSheetDocument document, string modName)
    {
        _document = document;
        ModName = modName;

        foreach (var tab in document.Tabs) Tabs.Add(tab);

        _tab = Tabs.FirstOrDefault();
        Build();

        AddRowCommand = new RelayCommand(_ => AddRow());
        DeleteRowCommand = new RelayCommand(p => DeleteRow(p as DataRowView));
    }

    public string ModName { get; }

    public string FileName => Path.GetFileName(_document.Path);

    public List<SourceSheetTab> Tabs { get; } = new();

    public bool HasTabs => Tabs.Count > 0;

    private SourceSheetTab? _tab;

    public SourceSheetTab? Tab
    {
        get => _tab;
        set
        {
            if (!SetProperty(ref _tab, value)) return;

            Build();
            OnPropertyChanged(nameof(TabSummary));
        }
    }

    private DataTable? _grid;

    /// <summary>
    /// A DataTable because the columns are not known until the file is open - there are
    /// 49 of them on a Chara sheet and 52 on a Thing sheet, and they differ per mod.
    /// </summary>
    public DataTable? Grid
    {
        get => _grid;
        private set => SetProperty(ref _grid, value);
    }

    public RelayCommand AddRowCommand { get; }
    public RelayCommand DeleteRowCommand { get; }

    public string TabSummary => _tab is null
        ? string.Empty
        : $"{_tab.Header.Count} columns · {_tab.Entries.Count} entries · data starts at row "
          + SourceSheetNames.FirstDataRow;

    private bool _dirty;

    public bool Dirty
    {
        get => _dirty;
        private set => SetProperty(ref _dirty, value);
    }

    /// <summary>What is wrong with the sheet as it stands, or null.</summary>
    public string? Warning
    {
        get
        {
            if (_tab is null || _grid is null) return null;

            var idColumn = _tab.IdColumn;
            if (idColumn < 0) return "This sheet has no id column, so the game cannot read it.";

            for (var i = 0; i < _grid.Rows.Count; i++)
            {
                var id = (_grid.Rows[i][idColumn] as string ?? string.Empty).Trim();
                if (id.Length != 0) continue;

                var below = 0;

                for (var j = i + 1; j < _grid.Rows.Count; j++)
                {
                    if ((_grid.Rows[j][idColumn] as string ?? string.Empty).Trim().Length > 0)
                        below++;
                }

                if (below == 0) break;

                var row = i + SourceSheetNames.FirstDataRow;

                return $"Row {row} has no id, and the game stops reading a sheet there - "
                       + $"the {below} {(below == 1 ? "entry" : "entries")} below it would "
                       + "never load. Give it an id or delete the row.";
            }

            return null;
        }
    }

    public bool HasWarning => Warning is not null;

    private void Build()
    {
        if (_tab is null)
        {
            Grid = null;
            return;
        }

        var table = new DataTable();

        for (var i = 0; i < _tab.Header.Count; i++)
        {
            // Duplicate column names are real - the official Thing sheet has "sort" twice -
            // and a DataTable will not take two of the same, so the later one is numbered.
            var name = Caption(i);
            var unique = name;
            var n = 2;

            while (table.Columns.Contains(unique)) unique = $"{name} ({n++})";

            table.Columns.Add(unique, typeof(string));
        }

        foreach (var entry in _tab.Entries) table.Rows.Add(entry.Cast<object>().ToArray());

        table.ColumnChanged += (_, _) => Touched();
        table.RowChanged += (_, _) => Touched();
        table.RowDeleted += (_, _) => Touched();

        Grid = table;
        OnPropertyChanged(nameof(TabSummary));
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }

    /// <summary>
    /// The column name with what it falls back to, so the least obvious rule about these
    /// sheets is visible where it matters rather than in documentation.
    /// </summary>
    private string Caption(int index)
    {
        var name = _tab!.Header[index];
        var fallback = index < _tab.Defaults.Count ? _tab.Defaults[index].Trim() : string.Empty;

        return fallback.Length == 0 ? name : $"{name}  ({fallback})";
    }

    private void Touched()
    {
        Dirty = true;
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }

    private void AddRow()
    {
        if (_grid is null || _tab is null) return;

        var row = _grid.NewRow();

        for (var i = 0; i < _grid.Columns.Count; i++) row[i] = string.Empty;

        _grid.Rows.Add(row);
        Touched();
    }

    private void DeleteRow(DataRowView? row)
    {
        if (row is null || _grid is null) return;

        row.Row.Delete();
        Touched();
    }

    /// <summary>
    /// Writes the sheet back, keeping a copy of what was there first. Returns false when
    /// nothing was written.
    /// </summary>
    public bool Save()
    {
        if (_tab is null || _grid is null) return false;

        _grid.AcceptChanges();

        _tab.Entries.Clear();

        foreach (DataRow row in _grid.Rows)
        {
            var cells = new string[_tab.Header.Count];

            for (var i = 0; i < cells.Length; i++)
                cells[i] = row[i] as string ?? string.Empty;

            _tab.Entries.Add(cells);
        }

        var backup = FileBackup.Take(_document.Path);

        try
        {
            _document.Save();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not save {_document.Path}", ex);

            MessageBox.Show(
                $"The sheet could not be saved.\n\n{ex.Message}"
                + (backup is not null ? $"\n\nThe file as it was is still at:\n{backup}" : ""),
                "Could not save", MessageBoxButton.OK, MessageBoxImage.Warning);

            return false;
        }

        Dirty = false;
        LastBackup = backup;
        OnPropertyChanged(nameof(LastBackup));

        return true;
    }

    public string? LastBackup { get; private set; }
}
