using System.IO;
using Path = System.IO.Path;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Services;
using ElinTextureManager.Core.Watching;

namespace ElinTextureManager.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private string? _badge;
    private bool _isSelected;

    public NavItem(string key, string label, string? tooltip = null)
    {
        Key = key;
        Label = label;
        Tooltip = tooltip;
        Icon = NavIcons.ForKey(key);
    }

    public string Key { get; }
    public string Label { get; }

    /// <summary>Line-art icon, drawn rather than taken from a font. See NavIcons.</summary>
    public System.Windows.Media.Geometry? Icon { get; }

    /// <summary>
    /// What this section is for, in one sentence. Several of these names only make
    /// sense once you already know the application, which is exactly when a tooltip
    /// is no longer any use - so they are written for a first-time reader.
    /// </summary>
    public string? Tooltip { get; }

    public string? Badge
    {
        get => _badge;
        set { SetProperty(ref _badge, value); OnPropertyChanged(nameof(HasBadge)); }
    }

    public bool HasBadge => !string.IsNullOrEmpty(_badge);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>The shell: navigation, the status bar, scanning and the workshop watcher.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _app;
    private readonly Dispatcher _dispatcher;
    private WorkshopWatcher? _watcher;
    private DispatcherTimer? _gameTimer;

    private object? _currentPage;
    private object? _previousPage;
    private string _statusText = "Starting...";
    private string? _activityText;
    private bool _isScanning;
    private double _scanProgress;
    private bool _elinRunning;
    private string? _changeNotice;
    private bool _restartNeeded;

    public MainViewModel(AppServices app, Dispatcher dispatcher)
    {
        _app = app;
        _dispatcher = dispatcher;

        // The rescan is passed in so that adding a portrait can bring itself into view
        // without the browser having to know what a scan is.
        Browser = new TextureBrowserViewModel(_app, OpenTextureDetail,
            () => RefreshAsync(userRequested: true));
        Overrides = new OverridesViewModel(_app, OnDataChanged, OpenTextureDetail);
        Mods = new ModsViewModel(_app, OpenModDetail, OnDataChanged);
        LoadOrder = new LoadOrderViewModel(_app, OnDataChanged);
        Settings = new SettingsViewModel(_app, OnPathsChanged, OnDisplayChanged);
        News = new NewsViewModel(_app);
        Health = new HealthViewModel(_app, OnDataChanged);
        Identify = new IdentifyViewModel(_app, OpenTextureDetail);
        Setups = new SetupsViewModel(_app, OnDataChanged);
        Bisect = new BisectViewModel(_app, OnDataChanged);
        DressUp = new DressUpViewModel(_app);
        Sheets = new SheetsViewModel(_app);

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(userRequested: true), () => !IsScanning);
        NavigateCommand = new RelayCommand(p => Navigate(p as string));
        BackCommand = new RelayCommand(GoBack, () => _previousPage is not null);
        FocusSearchCommand = new RelayCommand(() => SearchFocusRequested?.Invoke());
        OpenLogCommand = new RelayCommand(() => ShellService.OpenFolder(Core.Detection.AppPaths.LogDirectory));
        DismissNoticeCommand = new RelayCommand(() => ChangeNotice = null);

        BuildNav();
    }

    // ---- pages ----

    public TextureBrowserViewModel Browser { get; }
    public OverridesViewModel Overrides { get; }
    public ModsViewModel Mods { get; }
    public LoadOrderViewModel LoadOrder { get; }
    public SettingsViewModel Settings { get; }
    public NewsViewModel News { get; }

    public HealthViewModel Health { get; }

    public IdentifyViewModel Identify { get; }

    public SetupsViewModel Setups { get; }

    public BisectViewModel Bisect { get; }
    public GuideViewModel Guide { get; } = new();
    public SheetsViewModel Sheets { get; }

    public DressUpViewModel DressUp { get; }

    /// <summary>Every nav entry, in one list, for badges and selection.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = new();

    /// <summary>
    /// The same entries split into the three groups the sidebar draws under headings.
    /// Eleven flat items read as an undifferentiated list; three labelled groups of
    /// three or four read as a table of contents.
    /// </summary>
    public ObservableCollection<NavItem> LibraryNav { get; } = new();
    public ObservableCollection<NavItem> ManagementNav { get; } = new();
    public ObservableCollection<NavItem> SystemNav { get; } = new();

    public event Action? SearchFocusRequested;

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand FocusSearchCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand DismissNoticeCommand { get; }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool CanGoBack => _previousPage is not null;

    // ---- status bar ----

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ActivityText
    {
        get => _activityText;
        private set => SetProperty(ref _activityText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set { SetProperty(ref _isScanning, value); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !_isScanning;

    public double ScanProgress
    {
        get => _scanProgress;
        private set => SetProperty(ref _scanProgress, value);
    }

    public int ModCount => _app.Scan.TextureModCount;
    public int TotalModCount => _app.Scan.ModCount;
    public int TextureCount => _app.Scan.TextureFileCount;
    public int UniqueCount => _app.Scan.UniqueTextureCount;
    public int ConflictCount => _app.Scan.ConflictCount;
    public int OverrideCount => _app.Selections.Count;

    public bool ElinRunning
    {
        get => _elinRunning;
        private set => SetProperty(ref _elinRunning, value);
    }

    /// <summary>Set once a texture change lands, cleared when the game is next seen closed.</summary>
    public bool RestartNeeded
    {
        get => _restartNeeded;
        private set => SetProperty(ref _restartNeeded, value);
    }

    public string? ChangeNotice
    {
        get => _changeNotice;
        private set { SetProperty(ref _changeNotice, value); OnPropertyChanged(nameof(HasChangeNotice)); }
    }

    public bool HasChangeNotice => !string.IsNullOrEmpty(_changeNotice);

    public bool IsConfigured => _app.IsConfigured;

    public string ElinPathText => _app.Paths?.ElinRoot ?? "Elin not detected";

    // ---- startup ----

    public async Task StartAsync()
    {
        if (!_app.IsConfigured)
        {
            StatusText = "Elin was not found automatically.";
            Navigate("Settings");
            OnPropertyChanged(nameof(IsConfigured));
            return;
        }

        await RefreshAsync(userRequested: false);

        // Before anything else the user might do: if a mod search was interrupted, their
        // load order is currently an arrangement they never chose.
        var recovered = _app.Bisect.RecoverIfInterrupted();
        if (recovered is not null)
        {
            ChangeNotice = recovered;
            OnDataChanged();
        }

        Navigate(_app.Settings.FirstRunCompleted ? _app.Settings.LastPage : "AllTextures");

        if (!_app.Settings.FirstRunCompleted)
        {
            _app.Settings.FirstRunCompleted = true;
            _app.SaveSettings();
        }

        StartWatching();
        StartGameDetection();
    }

    /// <summary>
    /// Builds the sidebar. Browsing is one thing, deciding what the game loads is
    /// another, and the application's own settings are a third - so they are three
    /// labelled groups rather than one list of eleven.
    /// </summary>
    private void BuildNav()
    {
        NavItems.Clear();
        LibraryNav.Clear();
        ManagementNav.Clear();
        SystemNav.Clear();

        Add(LibraryNav, "AllTextures", "All Textures",
            "Every replacement image found in your installed mods.");
        Add(LibraryNav, "Characters", "Characters",
            "Sprites for NPCs, monsters and the player.");
        Add(LibraryNav, "Items", "Items", "Item sprites.");
        Add(LibraryNav, "Portraits", "Portraits",
            "Character portraits, grouped by what their file names encode.");
        Add(LibraryNav, "Objects", "Objects", "Furniture, walls and placed objects.");
        Add(LibraryNav, "DressUp", "Character Creator",
            "Build a character from the PCC parts across your mods and save it into the game.");
        Add(LibraryNav, "Sheets", "Source Sheets",
            "The spreadsheets mods add characters, items and recipes with.");
        Add(LibraryNav, "Pcc", "PCC Parts",
            "The layered parts characters are built from - hair, clothes, body, face. "
            + "Most character mods ship these and nothing else.");

        Add(ManagementNav, "Conflicts", "Conflicts",
            "Images supplied by more than one enabled mod. These are the ones you have "
            + "to make a decision about.");
        Add(ManagementNav, "Overrides", "Selected Overrides",
            "Images where you picked which mod wins. Your choices are copied into a "
            + "package of their own, so no Workshop folder is ever modified.");
        Add(ManagementNav, "Mods", "Mods",
            "Every detected mod, grouped by its Workshop tags. Turn a whole mod off here.");
        Add(ManagementNav, "LoadOrder", "Load Order",
            "The order Elin loads mods in, which decides who wins when you have not "
            + "chosen for yourself.");

        Add(ManagementNav, "Identify", "Identify",
            "Paste a screenshot and find which mod supplies what is in it.");
        Add(ManagementNav, "Bisect", "Find the Culprit",
            "Halve your mod list until the one that broke the game is left.");
        Add(ManagementNav, "Health", "Mod Health",
            "Broken calls, duplicate code and load-order rot - read from the mods themselves.");

        Add(SystemNav, "Setups", "Setups",
            "Named profiles of texture choices, and setup files that move one between machines.");
        Add(SystemNav, "Guide", "Modding Guide",
            "How a mod is put together, and the parts of it that fail quietly.");
        Add(SystemNav, "News", "Game News", "Elin's own Steam announcements.");
        Add(SystemNav, "Settings", "Settings", "Paths, scanning and appearance.");
    }

    private void Add(ObservableCollection<NavItem> group, string key, string label, string tooltip)
    {
        var item = new NavItem(key, label, tooltip);
        group.Add(item);
        NavItems.Add(item);
    }

    public void Navigate(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        _previousPage = null;
        OnPropertyChanged(nameof(CanGoBack));

        foreach (var item in NavItems) item.IsSelected = item.Key == key;

        // The dress-up preview animates on a timer. Leaving it running off-screen would
        // recompose a character four times a second that nobody is looking at.
        if (key != "DressUp") DressUp.Suspend();

        switch (key)
        {
            case "AllTextures":
                ShowBrowser("Texture Archive", null, TextureScope.All,
                    $"{UniqueCount:N0} images from {ModCount:N0} texture mods");
                break;
            case "Characters":
                ShowBrowser("Characters", TextureCategory.Characters, TextureScope.All,
                    "Sprites for NPCs, monsters and the player.");
                break;
            case "Items":
                ShowBrowser("Items", TextureCategory.Items, TextureScope.All,
                    "Item sprites.");
                break;
            case "Portraits":
                ShowBrowser("Portraits", TextureCategory.Portraits, TextureScope.All,
                    "Grouped by what the file names encode. Overlay layers are shown "
                    + "inside the portrait they belong to.");
                break;
            case "Objects":
                ShowBrowser("Objects", TextureCategory.Objects, TextureScope.All,
                    "Furniture, walls and placed objects.");
                break;
            case "Pcc":
                ShowBrowser("PCC Parts", TextureCategory.Pcc, TextureScope.All,
                    "Grouped by the layer each file draws. These are usually greyscale - "
                    + "Elin tints them per character - so the shape is what identifies them.");
                break;
            case "Conflicts":
                ShowBrowser("Conflicts", null, TextureScope.ConflictsOnly,
                    "Images supplied by more than one enabled mod. These are the ones "
                    + "that need a decision.");
                break;
            case "Overrides":
                Overrides.Apply();
                CurrentPage = Overrides;
                break;
            case "Mods":
                Mods.Apply();
                CurrentPage = Mods;
                break;
            case "LoadOrder":
                LoadOrder.Apply();
                CurrentPage = LoadOrder;
                break;
            case "Setups":
                Setups.Apply();
                CurrentPage = Setups;
                break;
            case "Identify":
                CurrentPage = Identify;
                break;
            case "DressUp":
                DressUp.Apply();
                CurrentPage = DressUp;
                break;
            case "Bisect":
                Bisect.Apply();
                CurrentPage = Bisect;
                break;
            case "Health":
                CurrentPage = Health;
                // Same pattern as News: show the page at once, fill it in when the
                // assembly read lands. It runs once and then only on demand.
                _ = Health.RunAsync();
                break;
            case "Sheets":
                CurrentPage = Sheets;
                _ = Sheets.LoadAsync();
                break;
            case "Guide":
                CurrentPage = Guide;
                break;
            case "News":
                CurrentPage = News;
                // Fire and forget: the page shows its cache at once and fills in when the
                // fetch lands, so navigation is never blocked on the network.
                _ = News.LoadAsync();
                break;
            case "Settings":
                Settings.RaisePathProperties();
                CurrentPage = Settings;
                break;
        }

        _app.Settings.LastPage = key;
        _app.SaveSettings();
    }

    private void ShowBrowser(string title, string? category, TextureScope scope, string? subtitle = null)
    {
        Browser.Title = title;
        Browser.Subtitle = subtitle;
        Browser.ModFilter = null;
        Browser.CategoryFilter = category;
        Browser.Scope = scope;
        Browser.RefreshPrefixes();
        Browser.Apply();
        CurrentPage = Browser;
    }

    private void OpenTextureDetail(TextureEntry entry)
    {
        _previousPage = CurrentPage;
        OnPropertyChanged(nameof(CanGoBack));
        CurrentPage = new TextureDetailViewModel(_app, entry, OnDataChanged);
    }

    private void OpenModDetail(ModPackage mod)
    {
        _previousPage = CurrentPage;
        OnPropertyChanged(nameof(CanGoBack));

        Browser.Title = mod.Name;
        Browser.Subtitle = BuildModSubtitle(mod);
        Browser.CategoryFilter = null;
        Browser.Scope = TextureScope.All;
        Browser.ModFilter = mod.Key;
        Browser.RefreshPrefixes();
        Browser.Apply();

        CurrentPage = Browser;
    }

    private string BuildModSubtitle(ModPackage mod)
    {
        var ids = mod.Textures.Where(t => !t.IsVariant).Select(t => t.TextureId).Distinct().ToList();
        var conflicts = ids.Count(id =>
            _app.Scan.Index.TryGetValue(id, out var e) && e.HasConflict);

        var workshop = mod.WorkshopId is null ? "" : $"Workshop ID {mod.WorkshopId}  ·  ";
        return $"{workshop}{mod.TextureCount} textures  ·  {conflicts} conflicts  ·  "
               + $"{ids.Count - conflicts} unique";
    }

    public void GoBack()
    {
        if (_previousPage is null) return;

        // Rebuild the list BEFORE the page is shown. Refilling the collection resets the
        // panel's offset, so doing it after the view exists would scroll it back to the
        // top - exactly what the remembered offset is there to prevent.
        if (_previousPage is TextureBrowserViewModel b) b.Apply(preserveScroll: true);
        else if (_previousPage is OverridesViewModel o) o.Apply();
        else if (_previousPage is ModsViewModel m) m.Apply(preserveScroll: true);

        CurrentPage = _previousPage;
        _previousPage = null;
        OnPropertyChanged(nameof(CanGoBack));
    }

    // ---- scanning ----

    public async Task RefreshAsync(bool userRequested)
    {
        if (!_app.IsConfigured)
        {
            StatusText = "Set your Elin folder in Settings first.";
            return;
        }

        IsScanning = true;
        ActivityText = "Scanning...";
        ScanProgress = 0;

        var progress = new Progress<ScanProgress>(p =>
        {
            ActivityText = p.Total > 1 ? $"{p.Stage} {p.Done}/{p.Total}" : p.Stage;
            ScanProgress = p.Fraction * 100;
        });

        try
        {
            await _app.RefreshAsync(progress);

            // The library changed underneath it, so anything Health found is about a
            // set of mods that no longer exists.
            Health.Invalidate();

            StatusText = _app.Scan.Errors.Count == 0
                ? "Up to date"
                : $"Up to date · {_app.Scan.Errors.Count} items skipped (see log)";

            if (userRequested) ChangeNotice = null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Scan failed", ex);
            StatusText = "Scan failed - see the log.";
        }
        finally
        {
            IsScanning = false;
            ActivityText = null;
            ScanProgress = 0;
            OnDataChanged();
        }
    }

    /// <summary>Refreshes every count and re-applies the current page's filters.</summary>
    private void OnDataChanged()
    {
        OnPropertyChanged(nameof(ModCount));
        OnPropertyChanged(nameof(TotalModCount));
        OnPropertyChanged(nameof(TextureCount));
        OnPropertyChanged(nameof(UniqueCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(OverrideCount));
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ElinPathText));

        foreach (var item in NavItems)
        {
            item.Badge = item.Key switch
            {
                "Conflicts" => ConflictCount > 0 ? ConflictCount.ToString() : null,
                "Overrides" => OverrideCount > 0 ? OverrideCount.ToString() : null,
                // Only after a run: an empty badge here means "not checked", not "clean".
                "Health" => Health.BrokenCount > 0 ? Health.BrokenCount.ToString() : null,
                _ => null,
            };
        }

        // A rescan or an override change refreshes the page in place, so keep the user
        // where they were instead of throwing them back to the top.
        if (CurrentPage is TextureBrowserViewModel b) b.Apply(preserveScroll: true);
        else if (CurrentPage is OverridesViewModel o) o.Apply();
        else if (CurrentPage is ModsViewModel m) m.Apply(preserveScroll: true);
        else if (CurrentPage is LoadOrderViewModel l) l.Apply();

        // The Mods page and the Load Order page are two views of the same file, so a
        // change on one has to be reflected on the other. Rebuilding it here rather
        // than only on navigation keeps them from ever showing different answers -
        // unless it is holding unsaved edits, which are the user's and not ours to
        // discard.
        if (CurrentPage is not LoadOrderViewModel && !LoadOrder.IsDirty) LoadOrder.Apply();

        if (_app.Selections.Count > 0 && ElinRunning) RestartNeeded = true;
    }

    private void OnPathsChanged()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ElinPathText));
        StopWatching();
        _ = RefreshAsync(userRequested: true).ContinueWith(_ => _dispatcher.Invoke(StartWatching));
    }

    private void OnDisplayChanged()
    {
        if (CurrentPage is TextureBrowserViewModel b) b.Apply();
    }

    // ---- workshop watching ----

    public void StartWatching()
    {
        if (!_app.Settings.WatchFileChanges) return;

        var root = _app.Paths?.WorkshopRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        StopWatching();

        _watcher = new WorkshopWatcher(root);
        _watcher.Changed += OnWorkshopChanged;
        _watcher.Start();
    }

    public void StopWatching()
    {
        if (_watcher is null) return;

        _watcher.Changed -= OnWorkshopChanged;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnWorkshopChanged(WorkshopChange change)
    {
        _dispatcher.BeginInvoke(async () =>
        {
            var known = _app.Scan.Mods.Any(m =>
                string.Equals(m.WorkshopId, change.ModId, StringComparison.Ordinal));

            ChangeNotice = change.Kind switch
            {
                WorkshopChangeKind.ModRemoved => $"Workshop mod removed ({change.ModId}).",
                _ when !known => $"New Workshop mod detected ({change.ModId}).",
                _ => $"Workshop mod updated ({change.ModId}).",
            };

            StatusText = "Workshop change detected";

            if (_app.Settings.AutoRefreshWorkshop && !IsScanning)
                await RefreshAsync(userRequested: false);
        });
    }

    // ---- running game detection ----

    private void StartGameDetection()
    {
        _gameTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5),
        };

        _gameTimer.Tick += (_, _) =>
        {
            var running = GameProcessDetector.IsElinRunning();
            if (running == ElinRunning) return;

            ElinRunning = running;

            // Once the game closes, whatever was pending has been picked up on next launch.
            if (!running) RestartNeeded = false;
        };

        _gameTimer.Start();
        ElinRunning = GameProcessDetector.IsElinRunning();
    }

    public void Dispose()
    {
        StopWatching();
        _gameTimer?.Stop();
    }
}
