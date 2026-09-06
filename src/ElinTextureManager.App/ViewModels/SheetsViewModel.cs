using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Services;
using ElinTextureManager.Core.Sheets;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One workbook found in the library.</summary>
public sealed class SheetFileViewModel
{
    public required string Path { get; init; }
    public required string ModName { get; init; }
    public required string Tabs { get; init; }
    public required int Entries { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The language folder, when it is in one - mods ship EN and CN copies.</summary>
    public string Where
    {
        get
        {
            var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? string.Empty);
            return folder.Length == 0 ? string.Empty : folder;
        }
    }

    public string Summary => $"{Tabs} · {Entries} entries";
}

/// <summary>
/// Every source sheet in the library, and a way into them.
///
/// These are the files that add characters, items and the rest, and until now nothing
/// could see them: the texture scan indexes images, and a spreadsheet is not one. On a
/// large library there are a few hundred of them and they are the only place most content
/// actually lives.
/// </summary>
public sealed class SheetsViewModel : ObservableObject
{
    private readonly AppServices _app;

    public SheetsViewModel(AppServices app)
    {
        _app = app;

        OpenCommand = new RelayCommand(p => Open(p as SheetFileViewModel));
        OpenFolderCommand = new RelayCommand(p =>
        {
            if (p is SheetFileViewModel file)
                ShellService.OpenFolder(Path.GetDirectoryName(file.Path) ?? file.Path);
        });
    }

    public ObservableCollection<SheetFileViewModel> Files { get; } = new();

    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private bool _loading;

    public bool Loading
    {
        get => _loading;
        private set { SetProperty(ref _loading, value); OnPropertyChanged(nameof(Idle)); }
    }

    public bool Idle => !_loading;

    private string _searchText = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Show(); }
    }

    public int Count => Files.Count;

    public string Subtitle =>
        "The spreadsheets mods add characters, items, recipes and the rest with. Open one "
        + "to edit its entries - the header rows the game reads are kept exactly as they are.";

    private List<SheetFileViewModel> _all = new();

    /// <summary>Reads the library once, off the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (_all.Count > 0 || Loading) return;

        Loading = true;

        try
        {
            var mods = _app.Scan.Mods
                .Where(m => m.SourceType != TextureSourceType.Vanilla)
                .Select(m => (m.Name, m.Directory))
                .ToList();

            _all = await Task.Run(() => Find(mods));
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not list source sheets", ex);
            _all = new List<SheetFileViewModel>();
        }

        Loading = false;
        Show();
    }

    private static List<SheetFileViewModel> Find(List<(string Name, string Directory)> mods)
    {
        var found = new List<SheetFileViewModel>();

        foreach (var (name, directory) in mods)
        {
            List<string> books;

            try
            {
                books = Directory.GetFiles(directory, "*.xlsx", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                    .ToList();
            }
            catch { continue; }

            foreach (var book in books)
            {
                List<XlsxSheet> sheets;

                try { sheets = XlsxWorkbook.Read(book); }
                catch { continue; }

                var data = sheets.Where(s => SourceSheetNames.IsData(s.Name)).ToList();
                if (data.Count == 0) continue;

                var entries = data.Sum(s =>
                    Math.Max(0, s.LastContentRow - SourceSheetNames.DefaultRow));

                found.Add(new SheetFileViewModel
                {
                    Path = book,
                    ModName = name,
                    Tabs = string.Join(", ", data.Select(s => s.Name)),
                    Entries = entries,
                });
            }
        }

        return found
            .OrderByDescending(f => f.Entries)
            .ThenBy(f => f.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Show()
    {
        Files.Clear();

        var query = _searchText.Trim();

        foreach (var file in _all)
        {
            if (query.Length > 0
                && !file.ModName.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !file.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !file.Tabs.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Files.Add(file);
        }

        OnPropertyChanged(nameof(Count));
    }

    private void Open(SheetFileViewModel? file)
    {
        if (file is null) return;

        SourceSheetDocument document;

        try
        {
            document = SourceSheetDocument.Load(file.Path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{file.FileName} could not be opened.\n\n{ex.Message}",
                "Could not open it", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (document.Tabs.Count == 0)
        {
            MessageBox.Show(
                $"{file.FileName} has no sheet the game reads as source data.",
                "Nothing to edit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new Views.SheetEditorWindow(new SheetEditorViewModel(document, file.ModName))
        {
            Owner = Application.Current?.MainWindow,
        };

        window.ShowDialog();
    }
}
