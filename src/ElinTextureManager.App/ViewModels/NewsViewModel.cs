using System.Collections.ObjectModel;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.News;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One announcement card.</summary>
public sealed class NewsItemViewModel : ObservableObject
{
    private bool _expanded;

    public NewsItemViewModel(NewsItem item) => Item = item;

    public NewsItem Item { get; }

    public string Title => Item.Title;
    public string DateText => Item.DateText;
    public string Author => string.IsNullOrWhiteSpace(Item.Author) ? "Lafrontier" : Item.Author;
    public string FeedLabel => Item.FeedLabel;
    public string Url => Item.Url;
    public bool IsPatchNote => Item.IsPatchNote;

    public string Body => Item.Contents;

    /// <summary>First few lines, which is usually enough to know whether to read on.</summary>
    public string Summary
    {
        get
        {
            var body = Item.Contents;
            if (body.Length <= 260) return body;

            var cut = body.LastIndexOf(' ', 260);
            return body[..(cut > 120 ? cut : 260)].TrimEnd() + "...";
        }
    }

    public bool CanExpand => Item.Contents.Length > 260;

    public bool Expanded
    {
        get => _expanded;
        set => SetProperty(ref _expanded, value);
    }
}

/// <summary>
/// The News page: Elin's own Steam announcements, so patch notes that break texture mods
/// are visible next to the mods themselves.
///
/// This is the only page that uses the network, and it does nothing until the user allows
/// it in Settings. The last response is cached, so the page still has content offline.
/// </summary>
public sealed class NewsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly SteamNewsClient _client = new();

    private bool _isLoading;
    private string? _statusMessage;
    private DateTimeOffset? _fetchedUtc;

    public NewsViewModel(AppServices app)
    {
        _app = app;

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(force: true), () => !IsLoading);
        OpenPostCommand = new RelayCommand(p => ShellService.OpenUrl((p as NewsItemViewModel)?.Url));
        ToggleExpandCommand = new RelayCommand(p =>
        {
            if (p is NewsItemViewModel item) item.Expanded = !item.Expanded;
        });
    }

    public ObservableCollection<NewsItemViewModel> Items { get; } = new();

    /// <summary>Mods Steam has touched recently, which is local knowledge and needs no network.</summary>
    public ObservableCollection<ModRecentViewModel> RecentMods { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenPostCommand { get; }
    public RelayCommand ToggleExpandCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !_isLoading;

    public bool NewsEnabled => _app.Settings.EnableSteamNews;

    public bool NewsDisabled => !_app.Settings.EnableSteamNews;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public string FetchedText => _fetchedUtc is null
        ? "never fetched"
        : "last checked " + _fetchedUtc.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Shows the cache immediately, then fetches when allowed. <paramref name="force"/> is
    /// the Refresh button; without it a fetch is skipped while a recent cache exists, so
    /// visiting the page repeatedly does not hammer Steam.
    /// </summary>
    public async Task LoadAsync(bool force = false)
    {
        RefreshRecentMods();

        var cached = NewsCache.Load(AppPaths.NewsCacheFile);
        if (cached is not null)
        {
            Show(cached.Items);
            _fetchedUtc = cached.FetchedUtc;
            OnPropertyChanged(nameof(FetchedText));
        }

        OnPropertyChanged(nameof(NewsEnabled));
        OnPropertyChanged(nameof(NewsDisabled));

        if (!_app.Settings.EnableSteamNews)
        {
            StatusMessage = Items.Count == 0
                ? "Steam news is turned off in Settings, and nothing has been cached yet."
                : "Steam news is turned off in Settings. Showing the last cached fetch.";
            return;
        }

        var fresh = cached is not null
                    && DateTimeOffset.UtcNow - cached.FetchedUtc < TimeSpan.FromHours(6);

        if (fresh && !force)
        {
            StatusMessage = null;
            return;
        }

        IsLoading = true;
        StatusMessage = null;

        try
        {
            var result = await _client.FetchAsync();

            if (result.Success && result.Items.Count > 0)
            {
                Show(result.Items);
                NewsCache.Save(AppPaths.NewsCacheFile, result.Items);
                _fetchedUtc = DateTimeOffset.UtcNow;
                OnPropertyChanged(nameof(FetchedText));
            }
            else
            {
                StatusMessage = Items.Count == 0
                    ? "Could not reach Steam, and nothing has been cached yet."
                    : "Could not reach Steam. Showing the last cached fetch.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(IReadOnlyList<NewsItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(new NewsItemViewModel(item));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The ten most recently changed mod folders - a purely local signal.</summary>
    private void RefreshRecentMods()
    {
        RecentMods.Clear();

        foreach (var mod in _app.Scan.Mods
                     .Where(m => m.SourceType == TextureSourceType.Workshop)
                     .Where(m => m.LastModifiedUtc != default)
                     .OrderByDescending(m => m.LastModifiedUtc)
                     .Take(10))
        {
            RecentMods.Add(new ModRecentViewModel(mod));
        }
    }
}

/// <summary>A recently updated mod, shown beside the news.</summary>
public sealed class ModRecentViewModel
{
    public ModRecentViewModel(ModPackage mod) => Mod = mod;

    public ModPackage Mod { get; }

    public string Name => Mod.Name;
    public string? WorkshopId => Mod.WorkshopId;
    public bool Enabled => Mod.Enabled;

    public string UpdatedText => Mod.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string DetailText => Mod.TextureCount == 0
        ? "no replacement images"
        : Mod.TextureCount + " images";
}
