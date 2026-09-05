using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Portraits;
using ElinTextureManager.Core.Storage;
using Microsoft.Win32;

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
    private readonly Func<Task>? _rescan;

    private string _searchText = string.Empty;
    private TextureScope _scope = TextureScope.All;
    private string? _categoryFilter;
    private string? _prefixFilter;
    private string? _modFilter;
    private string _title = "All Textures";
    private string? _subtitle;

    public TextureBrowserViewModel(AppServices app, Action<TextureEntry> openDetail,
        Func<Task>? rescan = null)
    {
        _app = app;
        _openDetail = openDetail;
        _rescan = rescan;

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

        AddPortraitCommand = new RelayCommand(_ => AddPortrait());
        RestartCommand = new RelayCommand(_ => Restart());
        DismissAddedNoticeCommand = new RelayCommand(_ => AddedNotice = null);
    }

    public ObservableCollection<TextureCardViewModel> Items { get; } = new();

    public ObservableCollection<string> AvailablePrefixes { get; } = new();

    /// <summary>The "no filter" row, kept in one place so the view and the list agree.</summary>
    public const string AllPrefixes = "All groups";

    private string _selectedPrefixDisplay = AllPrefixes;

    /// <summary>
    /// The groups on offer for what this page shows. Rebuilt whenever the scope changes,
    /// because a family worth its own line across the library can be a single image once
    /// the page is narrowed to portraits.
    /// </summary>
    private GroupIndex? _groups;

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
    public RelayCommand AddPortraitCommand { get; }

    /// <summary>
    /// Only the portraits page offers this. The same view model backs every category
    /// page, and "Add portrait" on the Items page would be a button that lies.
    /// </summary>
    public bool CanAddPortrait =>
        string.Equals(_categoryFilter, TextureCategory.Portraits, StringComparison.Ordinal);

    public RelayCommand RestartCommand { get; }
    public RelayCommand DismissAddedNoticeCommand { get; }

    private string? _statusMessage;

    /// <summary>What the last thing the user did came to. Null when there is nothing to say.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private string? _addedNotice;

    /// <summary>
    /// Shown with a Restart button after portraits are added. Null the rest of the time.
    /// </summary>
    public string? AddedNotice
    {
        get => _addedNotice;
        private set => SetProperty(ref _addedNotice, value);
    }

    private void Restart()
    {
        if (Application.Current is App running) running.Restart();
    }

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
        set
        {
            if (!SetProperty(ref _categoryFilter, value)) return;

            OnPropertyChanged(nameof(CanAddPortrait));
            Apply();
        }
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

    /// <summary>
    /// Adds a portrait of the user's own to Elin\Custom\Portrait, which the game offers
    /// in its portrait picker alongside the built-in ones.
    ///
    /// Deliberately additive: nothing already in the folder is written over, and no
    /// vanilla portrait is replaced. A name that is taken gets a number rather than
    /// taking the other portrait's place.
    /// </summary>
    private void AddPortrait()
    {
        if (_app.Paths is null)
        {
            StatusMessage = "Elin has not been found yet, so there is nowhere to put it.";
            return;
        }

        var model = new PortraitImportViewModel(_app.Paths);

        var window = new Views.PortraitImportWindow(model)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true || model.Picture is null) return;

        string written;

        try
        {
            written = PortraitWriter.Install(_app.Paths, model.Picture, model.ChosenId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The portrait could not be saved.\n\n{ex.Message}",
                "Could not add it", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage =
            $"Added {Path.GetFileName(written)} to {PortraitWriter.FolderFor(_app.Paths)}. "
            + "It is an addition, so nothing already there was replaced. Elin offers it "
            + "in its portrait picker next time the game starts.";

        AddedNotice = "The portrait you added is being brought into the list below.";

        // Brought into view rather than left to be looked for. If the rescan does not
        // manage it - a thumbnail already cached under the same name, say - the notice
        // beside this offers a restart, which rebuilds everything from the folder.
        BringAddedIntoView();
    }

    private async void BringAddedIntoView()
    {
        if (_rescan is null) return;

        try
        {
            await _rescan();
            SearchText = string.Empty;
            Scope = TextureScope.All;
            SelectedPrefixDisplay = AllPrefixes;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not rescan after adding a portrait", ex);
            AddedNotice = "They are on disk, but the list could not be rebuilt. "
                          + "Restarting will pick them up.";
        }
    }

    public int ResultCount => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Headline of the empty state. Says which kind of empty this is.</summary>
    public string EmptyTitle => _app.Scan.UniqueTextureCount == 0
        ? "Nothing scanned yet"
        : _scope == TextureScope.ConflictsOnly ? "No conflicts"
        : _scope == TextureScope.SelectedOnly ? "No overrides yet"
        : "Nothing matches";

    public string EmptyMessage => _app.Scan.UniqueTextureCount == 0
        ? "No replacement images have been found. Use Refresh Mods to scan your Workshop folder."
        : _scope == TextureScope.ConflictsOnly
            ? "Every image in this view comes from exactly one mod, so there is nothing to decide between."
            : _scope == TextureScope.SelectedOnly
                ? "You have not chosen a winner for any image in this view. Open one and press Use This Texture."
                : "No images match the current search and filters. Try Clear filters.";

    public int ThumbnailSize => (int)_app.Settings.ThumbnailSize;

    public double CardWidth => ThumbnailSize + 18;

    public double CardHeight => ThumbnailSize + 92;

    // ---- grid density ----

    /// <summary>
    /// How many images fit on screen at once, which for a library of thousands is the
    /// difference between browsing and hunting. The setting already existed under
    /// Settings; it belongs on the toolbar, next to the grid it governs.
    /// </summary>
    public bool GridSmall
    {
        get => _app.Settings.ThumbnailSize == Core.Storage.ThumbnailSize.Small;
        set { if (value) SetGridSize(Core.Storage.ThumbnailSize.Small); }
    }

    public bool GridMedium
    {
        get => _app.Settings.ThumbnailSize == Core.Storage.ThumbnailSize.Medium;
        set { if (value) SetGridSize(Core.Storage.ThumbnailSize.Medium); }
    }

    public bool GridLarge
    {
        get => _app.Settings.ThumbnailSize == Core.Storage.ThumbnailSize.Large;
        set { if (value) SetGridSize(Core.Storage.ThumbnailSize.Large); }
    }

    private void SetGridSize(Core.Storage.ThumbnailSize size)
    {
        if (_app.Settings.ThumbnailSize == size) return;

        _app.Settings.ThumbnailSize = size;
        _app.SaveSettings();

        OnPropertyChanged(nameof(GridSmall));
        OnPropertyChanged(nameof(GridMedium));
        OnPropertyChanged(nameof(GridLarge));

        // Card size feeds the decode width, so the tiles have to be rebuilt.
        Apply();
    }

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

        // Filter and sort on the group actually offered in the dropdown, not on the raw
        // prefix - otherwise picking "azurlane" would match nothing, because no single
        // file's prefix is the word "azurlane".
        // Rebuilt every time rather than cached: the scope changes from under this when
        // the Conflicts or Overrides segment is picked, and a stale index would filter
        // on families that no longer exist in what is being shown.
        var groups = _groups = TextureGrouping.Build(Scoped());

        if (_prefixFilter is not null)
            entries = entries.Where(e =>
                string.Equals(groups.GroupOf(e), _prefixFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in entries.OrderBy(e => groups.GroupOf(e), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.Prefix, StringComparer.OrdinalIgnoreCase)
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
        OnPropertyChanged(nameof(EmptyTitle));
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
        AvailablePrefixes.Add(AllPrefixes);

        _groups = TextureGrouping.Build(Scoped());

        string? stillThere = null;

        foreach (var (prefix, count) in _groups.Groups)
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
        if (string.IsNullOrWhiteSpace(display) || display.StartsWith(AllPrefixes, StringComparison.Ordinal))
        {
            PrefixFilter = null;
            return;
        }

        var cut = display.LastIndexOf(" (", StringComparison.Ordinal);
        PrefixFilter = cut > 0 ? display[..cut] : display;
    }
}
