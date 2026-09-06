using System.IO;
using Path = System.IO.Path;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.ImportExport;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Services;
using Microsoft.Win32;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One row on the Selected Overrides page.</summary>
public sealed class OverrideRowViewModel : ObservableObject
{
    private BitmapSource? _thumbnail;

    public OverrideRowViewModel(OverrideStatus status, string overrideFilePath)
    {
        Status = status;
        OverrideFilePath = overrideFilePath;
        LoadThumbnail();
    }

    public OverrideStatus Status { get; }
    public OverrideSelection Selection => Status.Selection;
    public string OverrideFilePath { get; }

    /// <summary>The stored key, used for lookups and removal.</summary>
    public string TextureId => Selection.TextureId;

    /// <summary>The same ID without its index namespace, for display.</summary>
    public string DisplayId => Core.Model.TextureIdentity.Display(Selection.TextureId);
    public string SourceModName => Selection.SourceModName;
    public string? WorkshopId => Selection.SourceWorkshopId;
    public string SourcePath => Selection.SourcePath;
    public string SelectedDate => Selection.SelectedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public bool IsOk => Status.State == OverrideState.Ok;
    public bool NeedsAttention => Status.State != OverrideState.Ok;

    public string StateLabel => Status.State switch
    {
        OverrideState.SourceUpdated => "SOURCE UPDATED",
        OverrideState.SourceMissing => "SOURCE MOD NOT INSTALLED",
        OverrideState.FileMissing => "OVERRIDE FILE MISSING",
        _ => "Active",
    };

    public string StateDetail => Status.State switch
    {
        OverrideState.SourceUpdated =>
            $"The Workshop version of {TextureId} changed since you selected it.",
        OverrideState.SourceMissing =>
            "The mod this came from is no longer installed. Your override still works.",
        OverrideState.FileMissing =>
            "The copied texture is missing from the override package.",
        _ => string.Empty,
    };

    /// <summary>Only meaningful when the source is still installed and has changed.</summary>
    public bool CanUpdateToNewVersion => Status.State == OverrideState.SourceUpdated
                                         && Status.CurrentSource is not null;

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    private async void LoadThumbnail()
    {
        var path = File.Exists(OverrideFilePath) ? OverrideFilePath : Selection.SourcePath;
        Thumbnail = await TextureImageLoader.LoadAsync(path, 128);
    }
}

/// <summary>The Selected Overrides page.</summary>
public sealed class OverridesViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onChanged;
    private readonly Action<TextureEntry> _openDetail;
    private string _statusMessage = string.Empty;

    public OverridesViewModel(AppServices app, Action onChanged, Action<TextureEntry> openDetail)
    {
        _app = app;
        _onChanged = onChanged;
        _openDetail = openDetail;

        RemoveCommand = new RelayCommand(p => Remove(p as OverrideRowViewModel));
        ChangeCommand = new RelayCommand(p => OpenComparison(p as OverrideRowViewModel));
        UpdateToNewCommand = new RelayCommand(p => UpdateToNew(p as OverrideRowViewModel));
        OpenSourceFolderCommand = new RelayCommand(p =>
            ShellService.RevealFile((p as OverrideRowViewModel)?.SourcePath));
        RevealOverrideCommand = new RelayCommand(p =>
            ShellService.RevealFile((p as OverrideRowViewModel)?.OverrideFilePath));
        OpenOverrideFolderCommand = new RelayCommand(() =>
            ShellService.OpenFolder(_app.Paths?.OverrideTextureRoot));
        ClearAllCommand = new RelayCommand(ClearAll, () => Items.Count > 0);
        ExportCommand = new RelayCommand(Export, () => Items.Count > 0);
        ImportCommand = new RelayCommand(Import);
        ExportAsModCommand = new RelayCommand(ExportAsMod, () => Items.Count > 0);
    }

    public ObservableCollection<OverrideRowViewModel> Items { get; } = new();

    public RelayCommand RemoveCommand { get; }
    public RelayCommand ChangeCommand { get; }
    public RelayCommand UpdateToNewCommand { get; }
    public RelayCommand OpenSourceFolderCommand { get; }
    public RelayCommand RevealOverrideCommand { get; }
    public RelayCommand OpenOverrideFolderCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportAsModCommand { get; }

    public int Count => Items.Count;
    public bool IsEmpty => Items.Count == 0;
    public int AttentionCount => Items.Count(i => i.NeedsAttention);
    public bool HasAttention => AttentionCount > 0;

    public string? OverrideFolder => _app.Paths?.OverrideTextureRoot;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void Apply()
    {
        Items.Clear();

        if (_app.Overrides is null || _app.Paths is null) return;

        foreach (var status in _app.Overrides.Audit(_app.Scan))
        {
            var path = Path.Combine(_app.Paths.OverrideTextureRoot, status.Selection.FileName);
            Items.Add(new OverrideRowViewModel(status, path));
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(HasAttention));
    }

    private void Remove(OverrideRowViewModel? row)
    {
        if (row is null || _app.Overrides is null) return;

        var result = _app.Overrides.Remove(row.TextureId);
        StatusMessage = result.Message;

        RemoveOverrideFromIndex(row.TextureId, row.OverrideFilePath);
        _app.RecomputeWinners();
        Apply();
        _onChanged();
    }

    private void RemoveOverrideFromIndex(string textureId, string overridePath)
    {
        if (_app.Scan.Index.TryGetValue(textureId, out var entry))
        {
            var existing = entry.Versions.FirstOrDefault(v => v.SourceType == TextureSourceType.Override);
            if (existing is not null) entry.Versions.Remove(existing);
        }

        var mod = _app.Scan.Mods.FirstOrDefault(m => m.SourceType == TextureSourceType.Override);
        mod?.Textures.RemoveAll(t =>
            string.Equals(t.FullPath, overridePath, StringComparison.OrdinalIgnoreCase));

        TextureImageLoader.Invalidate(overridePath);
    }

    private void OpenComparison(OverrideRowViewModel? row)
    {
        if (row is null) return;
        if (_app.Scan.Index.TryGetValue(row.TextureId, out var entry)) _openDetail(entry);
    }

    private void UpdateToNew(OverrideRowViewModel? row)
    {
        if (row?.Status.CurrentSource is null || _app.Overrides is null) return;

        var result = _app.Overrides.Select(row.Status.CurrentSource);
        StatusMessage = result.Message;

        TextureImageLoader.Invalidate(row.OverrideFilePath);
        _app.RecomputeWinners();
        Apply();
        _onChanged();
    }

    private void ClearAll()
    {
        var answer = MessageBox.Show(
            $"Remove all {Items.Count} texture overrides?\n\n"
            + "This deletes only the copies inside ElinTextureManager_Overrides.\n"
            + "Your Workshop mods are not touched.",
            "Clear all overrides",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes || _app.Overrides is null) return;

        var (removed, failed) = _app.Overrides.ClearAll();
        StatusMessage = failed == 0
            ? $"Removed {removed} overrides."
            : $"Removed {removed} overrides; {failed} could not be removed (see the log).";

        foreach (var row in Items.ToList()) RemoveOverrideFromIndex(row.TextureId, row.OverrideFilePath);

        _app.RecomputeWinners();
        Apply();
        _onChanged();
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "ElinTextureSelections.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export texture selections",
        };

        if (dialog.ShowDialog() != true) return;

        StatusMessage = SelectionTransfer.Export(_app.Selections, dialog.FileName)
            ? $"Exported {Items.Count} selections."
            : "Export failed - see the log for details.";
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import texture selections",
        };

        if (dialog.ShowDialog() != true || _app.Overrides is null) return;

        var outcome = SelectionTransfer.Import(dialog.FileName, _app.Scan, _app.Overrides);
        StatusMessage = outcome.Message;

        if (outcome.Missing.Count > 0)
        {
            var names = string.Join("\n", outcome.Missing.Take(15)
                .Select(m => $"  {m.TextureId}  <-  {m.SourceModName}"));

            MessageBox.Show(
                $"{outcome.Restored} selections restored.\n\n"
                + $"{outcome.Missing.Count} could not be matched to an installed mod:\n\n{names}"
                + (outcome.Missing.Count > 15 ? "\n  ..." : ""),
                "Import finished",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        _onChanged();
    }

    private void ExportAsMod()
    {
        if (_app.Paths is null) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder for the exported override mod",
        };

        if (dialog.ShowDialog() != true) return;

        var target = Path.Combine(dialog.FolderName, "ElinTextureManager_Overrides");

        StatusMessage = SelectionTransfer.ExportAsMod(_app.Paths, target, "My Elin Texture Selection")
            ? $"Exported override mod to {target}."
            : "Export failed - see the log for details.";
    }
}
