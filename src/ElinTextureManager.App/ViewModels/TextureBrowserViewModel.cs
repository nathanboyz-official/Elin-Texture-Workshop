using System.Collections.ObjectModel;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.App.ViewModels;

public enum TextureScope
{
    All,
    ConflictsOnly,
    SelectedOnly,
    UnselectedOnly,
}

/// <summary>
/// The texture grid. The same view model backs All Textures, the category pages, the
/// Conflicts page and a single mod's texture list - only the preset filters differ.
/// </summary>
public sealed class TextureBrowserViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action<TextureEntry> _openDetail;

    private string _searchText = string.Empty;
    private TextureScope _scope = TextureScope.All;
    private string? _categoryFilter;
    private string? _prefixFilter;
    private string? _modFilter;
    private string _title = "All Textures";
    private string? _subtitle;

    public TextureBrowserViewModel(AppServices app, Action<TextureEntry> openDetail)
    {
        _app = app;
        _openDetail = openDetail;

        OpenCommand = new RelayCommand(p =>
        {
            if (p is TextureCardViewModel card) _openDetail(card.Entry);
        });

        ClearFiltersCommand = new RelayCommand(() =>
        {
            SearchText = string.Empty;
            Scope = TextureScope.All;
            PrefixFilter = null;
        });
    }

    public ObservableCollection<TextureCardViewModel> Items { get; } = new();

    public ObservableCollection<string> AvailablePrefixes { get; } = new();

    public RelayCommand OpenCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string? Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Apply(); }
    }

    public TextureScope Scope
    {
        get => _scope;
        set { if (SetProperty(ref _scope, value)) Apply(); }
    }

    public string? CategoryFilter
    {
        get => _categoryFilter;
        set { if (SetProperty(ref _categoryFilter, value)) Apply(); }
    }

    public string? PrefixFilter
    {
        get => _prefixFilter;
        set { if (SetProperty(ref _prefixFilter, value)) Apply(); }
    }

    public string? ModFilter
    {
        get => _modFilter;
        set { if (SetProperty(ref _modFilter, value)) Apply(); }
    }

    public int ResultCount => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    public string EmptyMessage => _app.Scan.UniqueTextureCount == 0
        ? "No replacement textures found yet. Use Refresh to scan your Workshop folder."
        : "No textures match the current filters.";

    public int ThumbnailSize => (int)_app.Settings.ThumbnailSize;

    public double CardWidth => ThumbnailSize + 24;

    public double CardHeight => ThumbnailSize + 96;

    /// <summary>Rebuilds the visible cards from the current scan and filters.</summary>
    public void Apply()
    {
        Items.Clear();

        var entries = _app.Scan.Index.Values.AsEnumerable();

        if (_scope == TextureScope.ConflictsOnly)
            entries = entries.Where(e => e.HasConflict);

        if (_categoryFilter is not null)
            entries = entries.Where(e => string.Equals(e.Category, _categoryFilter, StringComparison.Ordinal));

        if (_prefixFilter is not null)
            entries = entries.Where(e => string.Equals(e.Prefix, _prefixFilter, StringComparison.OrdinalIgnoreCase));

        if (_modFilter is not null)
            entries = entries.Where(e => e.AllSources.Any(v =>
                string.Equals(v.ModKey, _modFilter, StringComparison.OrdinalIgnoreCase)));

        foreach (var entry in entries.OrderBy(e => e.Prefix, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.NumericId ?? int.MaxValue)
                     .ThenBy(e => e.TextureId, StringComparer.OrdinalIgnoreCase))
        {
            var hasOverride = _app.Selections.Has(entry.TextureId);

            if (_scope == TextureScope.SelectedOnly && !hasOverride) continue;
            if (_scope == TextureScope.UnselectedOnly && hasOverride) continue;

            var winner = _app.Winners.GetValueOrDefault(entry.TextureId) ?? TextureWinner.None;
            var card = new TextureCardViewModel(entry, winner, _app.Aliases.Get(entry.TextureId), hasOverride);

            if (!card.Matches(_searchText)) continue;

            Items.Add(card);
        }

        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(ThumbnailSize));
        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(CardHeight));
    }

    /// <summary>Refreshes the prefix filter list from the current scan.</summary>
    public void RefreshPrefixes()
    {
        AvailablePrefixes.Clear();
        AvailablePrefixes.Add("All prefixes");

        foreach (var (prefix, count) in Core.Scanning.TextureIndexBuilder.PrefixHistogram(_app.Scan))
            AvailablePrefixes.Add($"{prefix} ({count})");
    }

    /// <summary>Turns the combo box's display string back into a prefix.</summary>
    public void SetPrefixFromDisplay(string? display)
    {
        if (string.IsNullOrWhiteSpace(display) || display.StartsWith("All prefixes", StringComparison.Ordinal))
        {
            PrefixFilter = null;
            return;
        }

        var cut = display.LastIndexOf(" (", StringComparison.Ordinal);
        PrefixFilter = cut > 0 ? display[..cut] : display;
    }
}
