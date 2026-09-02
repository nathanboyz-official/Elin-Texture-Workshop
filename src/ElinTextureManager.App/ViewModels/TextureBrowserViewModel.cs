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
            SelectedPrefixDisplay = AllPrefixes;
        });
    }

    public ObservableCollection<TextureCardViewModel> Items { get; } = new();

    public ObservableCollection<string> AvailablePrefixes { get; } = new();

    /// <summary>The "no filter" row, kept in one place so the view and the list agree.</summary>
    public const string AllPrefixes = "All prefixes";

    private string _selectedPrefixDisplay = AllPrefixes;

    /// <summary>
    /// Bound to the combo box. Held here rather than left to the control's own selection
    /// because the list is rebuilt whenever the page changes, and a rebuilt list would
    /// otherwise leave the box showing nothing at all.
    /// </summary>
    public string SelectedPrefixDisplay
    {
        get => _selectedPrefixDisplay;
        set
        {
            if (!SetProperty(ref _selectedPrefixDisplay, value ?? AllPrefixes)) return;
            SetPrefixFromDisplay(_selectedPrefixDisplay);
        }
    }

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

    /// <summary>
    /// Where the grid was scrolled to. Held here rather than in the view, because the
    /// view is rebuilt every time you navigate away and back.
    /// </summary>
    public double ScrollOffset { get; set; }

    /// <summary>
    /// Rebuilds the visible cards from the current scan and filters.
    /// </summary>
    /// <param name="preserveScroll">
    /// True when the same result set is being rebuilt - returning from a texture, or a
    /// background rescan - so the user keeps their place. False when the filters changed,
    /// where the old position would be meaningless.
    /// </param>
    public void Apply(bool preserveScroll = false)
    {
        if (!preserveScroll) ScrollOffset = 0;

        Items.Clear();

        var entries = Scoped();

        if (_prefixFilter is not null)
            entries = entries.Where(e => string.Equals(e.Prefix, _prefixFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in entries.OrderBy(e => e.Prefix, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.NumericId ?? int.MaxValue)
                     .ThenBy(e => e.TextureId, StringComparer.OrdinalIgnoreCase))
        {
            var hasOverride = _app.Selections.Has(entry.TextureId);

            if (_scope == TextureScope.SelectedOnly && !hasOverride) continue;
            if (_scope == TextureScope.UnselectedOnly && hasOverride) continue;

            var winner = _app.Winners.GetValueOrDefault(entry.TextureId) ?? TextureWinner.None;
            var card = new TextureCardViewModel(
                entry, winner, _app.Aliases.Get(entry.TextureId), hasOverride, ThumbnailSize);

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

    /// <summary>
    /// Everything this page is showing before the prefix filter is applied: the scope, the
    /// category and the mod, with attached portrait overlays left out.
    ///
    /// An overlay is a layer Elin draws on top of another portrait, not a picture in its
    /// own right, so it belongs inside its base rather than beside it in the grid. It is
    /// still reachable - the base portrait's page offers it - and a stray overlay with no
    /// base is left visible rather than being hidden with no way to reach it.
    /// </summary>
    private IEnumerable<TextureEntry> Scoped()
    {
        var entries = _app.Scan.Index.Values.Where(e => !e.IsAttachedOverlay);

        if (_scope == TextureScope.ConflictsOnly)
            entries = entries.Where(ScanResult.IsConflict);

        if (_categoryFilter is not null)
            entries = entries.Where(e => string.Equals(e.Category, _categoryFilter, StringComparison.Ordinal));

        if (_modFilter is not null)
            entries = entries.Where(e => e.AllSources.Any(v =>
                string.Equals(v.ModKey, _modFilter, StringComparison.OrdinalIgnoreCase)));

        return entries;
    }

    /// <summary>
    /// Rebuilds the prefix filter from what this page can actually show, rather than from
    /// the whole library. Offering "objC" on the Portraits page is an option that can only
    /// ever produce an empty grid.
    /// </summary>
    public void RefreshPrefixes()
    {
        var previous = _prefixFilter;

        AvailablePrefixes.Clear();
        AvailablePrefixes.Add("All prefixes");

        var histogram = Scoped()
            .GroupBy(e => e.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Prefix: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Prefix, StringComparer.OrdinalIgnoreCase);

        string? stillThere = null;

        foreach (var (prefix, count) in histogram)
        {
            var display = $"{prefix} ({count})";
            AvailablePrefixes.Add(display);
            if (string.Equals(prefix, previous, StringComparison.OrdinalIgnoreCase)) stillThere = display;
        }

        // A prefix that belonged to the page we came from would otherwise silently filter
        // this one down to nothing.
        if (previous is not null && stillThere is null) _prefixFilter = null;

        _selectedPrefixDisplay = stillThere ?? AllPrefixes;

        OnPropertyChanged(nameof(PrefixFilter));
        OnPropertyChanged(nameof(SelectedPrefixDisplay));
    }

    /// <summary>Turns the combo box's display string back into a prefix.</summary>
    private void SetPrefixFromDisplay(string? display)
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
