using System.IO;
using Path = System.IO.Path;
using System.Collections.ObjectModel;
using System.Windows;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One row in the load-order editor.</summary>
public sealed class LoadOrderRowViewModel : ObservableObject
{
    private bool _enabled;

    public LoadOrderRowViewModel(LoadOrderEntry entry, ModPackage? mod, int position)
    {
        Entry = entry;
        Mod = mod;
        _enabled = entry.Enabled;
        Position = position;
    }

    public LoadOrderEntry Entry { get; }
    public ModPackage? Mod { get; }

    private int _position;
    public int Position
    {
        get => _position;
        set { if (SetProperty(ref _position, value)) OnPropertyChanged(nameof(PositionText)); }
    }

    public string PositionText => (Position + 1).ToString();

    public string Name => Mod?.Name ?? Entry.FolderName;
    public string? WorkshopId => Mod?.WorkshopId ?? Entry.FolderName;
    public string Path => Entry.Path;

    public int TextureCount => Mod?.TextureCount ?? 0;
    public bool HasTextures => TextureCount > 0;

    public int ConflictCount { get; set; }

    /// <summary>True when the folder in loadorder.txt is no longer on disk.</summary>
    public bool IsMissing => Mod is null;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            Entry.Enabled = value;
        }
    }
}

/// <summary>
/// The load-order page. Editing here changes which whole mod wins a file conflict;
/// the per-texture override system is the recommended way to pick individual textures.
/// </summary>
public sealed class LoadOrderViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onChanged;

    private LoadOrderRowViewModel? _selected;
    private string _statusMessage = string.Empty;
    private bool _isDirty;

    public LoadOrderViewModel(AppServices app, Action onChanged)
    {
        _app = app;
        _onChanged = onChanged;

        MoveUpCommand = new RelayCommand(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => Move(+1), () => CanMove(+1));
        MoveToTopCommand = new RelayCommand(() => MoveTo(0), () => Selected is not null);
        MoveToBottomCommand = new RelayCommand(() => MoveTo(Items.Count - 1), () => Selected is not null);
        SaveCommand = new RelayCommand(Save, () => IsDirty);
        RestoreCommand = new RelayCommand(Restore);
        OpenFolderCommand = new RelayCommand(() => ShellService.OpenFolder(Selected?.Path));
        EnableAllCommand = new RelayCommand(() => SetAll(true));
        DisableAllCommand = new RelayCommand(() => SetAll(false));
    }

    public ObservableCollection<LoadOrderRowViewModel> Items { get; } = new();

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveToTopCommand { get; }
    public RelayCommand MoveToBottomCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand EnableAllCommand { get; }
    public RelayCommand DisableAllCommand { get; }

    public LoadOrderRowViewModel? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasLoadOrderFile => _app.Paths?.HasLoadOrderFile ?? false;

    public string LoadOrderPath => _app.Paths?.LoadOrderFile ?? "not found";

    /// <summary>
    /// Explains the convention in force, since Elin's own file gives no direction and
    /// the application would rather say so than imply certainty.
    /// </summary>
    public string ConventionNote =>
        _app.Settings.PriorityConvention == Core.Overrides.PriorityConvention.LaterWins
            ? "Entries lower in this list are treated as higher priority."
            : "Entries higher in this list are treated as higher priority.";

    public int EnabledCount => Items.Count(i => i.Enabled);

    public void Apply()
    {
        Items.Clear();

        var byPath = _app.Scan.Mods
            .Where(m => m.InLoadOrderFile)
            .ToDictionary(m => Core.Overrides.SafePath.Normalize(m.Directory) ?? m.Directory,
                StringComparer.OrdinalIgnoreCase);

        var conflictsByMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _app.Scan.Index.Values.Where(e => e.HasConflict))
        foreach (var v in entry.Versions)
            conflictsByMod[v.ModKey] = conflictsByMod.GetValueOrDefault(v.ModKey) + 1;

        for (var i = 0; i < _app.LoadOrder.Entries.Count; i++)
        {
            var entry = _app.LoadOrder.Entries[i];
            var key = Core.Overrides.SafePath.Normalize(entry.Path) ?? entry.Path;
            byPath.TryGetValue(key, out var mod);

            var row = new LoadOrderRowViewModel(entry, mod, i);
            if (mod is not null) row.ConflictCount = conflictsByMod.GetValueOrDefault(mod.Key);
            Items.Add(row);
        }

        IsDirty = false;
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(HasLoadOrderFile));
        OnPropertyChanged(nameof(LoadOrderPath));
        OnPropertyChanged(nameof(ConventionNote));
    }

    private bool CanMove(int delta)
    {
        if (Selected is null) return false;
        var index = Items.IndexOf(Selected);
        var target = index + delta;
        return index >= 0 && target >= 0 && target < Items.Count;
    }

    private void Move(int delta)
    {
        if (Selected is null) return;

        var index = Items.IndexOf(Selected);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Items.Count) return;

        MoveRow(index, target);
    }

    private void MoveTo(int target)
    {
        if (Selected is null) return;
        var index = Items.IndexOf(Selected);
        if (index < 0 || target < 0 || target >= Items.Count || index == target) return;

        MoveRow(index, target);
    }

    /// <summary>Moves a row and keeps the displayed positions in step.</summary>
    public void MoveRow(int from, int to)
    {
        if (from < 0 || to < 0 || from >= Items.Count || to >= Items.Count || from == to) return;

        var row = Items[from];
        Items.Move(from, to);

        for (var i = 0; i < Items.Count; i++) Items[i].Position = i;

        Selected = row;
        MarkDirty();
    }

    public void MarkDirty()
    {
        IsDirty = true;
        StatusMessage = "Unsaved changes. Nothing is written until you press Save.";
    }

    private void SetAll(bool enabled)
    {
        foreach (var item in Items) item.Enabled = enabled;
        OnPropertyChanged(nameof(EnabledCount));
        MarkDirty();
    }

    private void Save()
    {
        if (_app.Paths is null) return;

        var answer = MessageBox.Show(
            $"Save this load order to:\n{_app.Paths.LoadOrderFile}\n\n"
            + "A timestamped backup of the current file is created first.",
            "Save load order",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.OK);

        if (answer != MessageBoxResult.OK) return;

        // Rebuild the document in the order shown, preserving unparsed lines. Anything
        // added to the document since this page was built - the Mods page appends an
        // entry when you switch off a mod the game has never seen - is kept rather than
        // dropped, which is what rebuilding purely from the visible rows would do.
        var shown = Items.Select(r => r.Entry).ToList();
        var missing = _app.LoadOrder.Entries.Where(e => !shown.Contains(e)).ToList();

        if (missing.Count > 0)
            Core.Logging.AppLog.Info($"Keeping {missing.Count} load-order entries added since this page was opened.");

        _app.LoadOrder.Entries.Clear();
        foreach (var row in shown) _app.LoadOrder.Entries.Add(row);
        foreach (var extra in missing) _app.LoadOrder.Entries.Add(extra);

        var saved = LoadOrderFile.Save(_app.LoadOrder, AppPaths.BackupDirectory, out var backup);

        if (saved)
        {
            IsDirty = false;
            StatusMessage = backup is null
                ? "Load order saved."
                : $"Load order saved. Backup: {Path.GetFileName(backup)}";

            LoadOrderFile.ApplyTo(_app.LoadOrder, _app.Scan.Mods);
            _app.RecomputeWinners();
            _onChanged();
        }
        else
        {
            StatusMessage = "Save failed - the original file was left untouched. See the log.";
        }
    }

    private void Restore()
    {
        var backups = LoadOrderFile.ListBackups(AppPaths.BackupDirectory);

        if (backups.Count == 0)
        {
            StatusMessage = "No load order backups have been made yet.";
            return;
        }

        var newest = backups[0];

        var answer = MessageBox.Show(
            $"Restore the load order from:\n{newest.Name}\n"
            + $"({newest.LastWriteTime:yyyy-MM-dd HH:mm})\n\n"
            + "The current file is backed up first, so this is reversible.",
            "Restore load order backup",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK || _app.Paths is null) return;

        if (LoadOrderFile.Restore(newest.FullName, _app.Paths, AppPaths.BackupDirectory))
        {
            StatusMessage = $"Restored from {newest.Name}.";
            _app.LoadOrder = LoadOrderFile.Read(_app.Paths.LoadOrderFile);
            LoadOrderFile.ApplyTo(_app.LoadOrder, _app.Scan.Mods);
            _app.RecomputeWinners();
            Apply();
            _onChanged();
        }
        else
        {
            StatusMessage = "Restore failed. See the log.";
        }
    }
}
