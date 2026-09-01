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

    public NavItem(string key, string label, string glyph)
    {
        Key = key;
        Label = label;
        Glyph = glyph;
    }

    public string Key { get; }
    public string Label { get; }
    public string Glyph { get; }

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

        Browser = new TextureBrowserViewModel(_app, OpenTextureDetail);
        Overrides = new OverridesViewModel(_app, OnDataChanged, OpenTextureDetail);
        Mods = new ModsViewModel(_app, OpenModDetail);
        LoadOrder = new LoadOrderViewModel(_app, OnDataChanged);
        Settings = new SettingsViewModel(_app, OnPathsChanged, OnDisplayChanged);

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

    public ObservableCollection<NavItem> NavItems { get; } = new();

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

        Navigate(_app.Settings.FirstRunCompleted ? _app.Settings.LastPage : "AllTextures");

        if (!_app.Settings.FirstRunCompleted)
        {
            _app.Settings.FirstRunCompleted = true;
            _app.SaveSettings();
        }

        StartWatching();
        StartGameDetection();
    }

    private void BuildNav()
    {
        NavItems.Clear();
        NavItems.Add(new NavItem("AllTextures", "All Textures", ""));
        NavItems.Add(new NavItem("Characters", "Characters", ""));
        NavItems.Add(new NavItem("Items", "Items", ""));
        NavItems.Add(new NavItem("Portraits", "Portraits", ""));
        NavItems.Add(new NavItem("Objects", "Objects", ""));
        NavItems.Add(new NavItem("Conflicts", "Conflicts", ""));
        NavItems.Add(new NavItem("Overrides", "Selected Overrides", ""));
        NavItems.Add(new NavItem("Mods", "Mods", ""));
        NavItems.Add(new NavItem("LoadOrder", "Load Order", ""));
        NavItems.Add(new NavItem("Settings", "Settings", ""));
    }

    public void Navigate(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        _previousPage = null;
        OnPropertyChanged(nameof(CanGoBack));

        foreach (var item in NavItems) item.IsSelected = item.Key == key;

        switch (key)
        {
            case "AllTextures":
                ShowBrowser("All Textures", null, TextureScope.All);
                break;
            case "Characters":
                ShowBrowser("Characters", TextureCategory.Characters, TextureScope.All);
                break;
            case "Items":
                ShowBrowser("Items", TextureCategory.Items, TextureScope.All);
                break;
            case "Portraits":
                ShowBrowser("Portraits", TextureCategory.Portraits, TextureScope.All);
                break;
            case "Objects":
                ShowBrowser("Objects", TextureCategory.Objects, TextureScope.All);
                break;
            case "Conflicts":
                ShowBrowser("Conflicts", null, TextureScope.ConflictsOnly,
                    "Textures supplied by more than one enabled mod. Click one to compare versions.");
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

        CurrentPage = _previousPage;
        _previousPage = null;
        OnPropertyChanged(nameof(CanGoBack));

        if (CurrentPage is TextureBrowserViewModel b) b.Apply();
        else if (CurrentPage is OverridesViewModel o) o.Apply();
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
                _ => null,
            };
        }

        if (CurrentPage is TextureBrowserViewModel b) b.Apply();
        else if (CurrentPage is OverridesViewModel o) o.Apply();
        else if (CurrentPage is ModsViewModel m) m.Apply();

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
