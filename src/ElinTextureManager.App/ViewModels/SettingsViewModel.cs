using System.IO;
using Path = System.IO.Path;
using System.Windows;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Services;
using ElinTextureManager.Core.Storage;
using Microsoft.Win32;

namespace ElinTextureManager.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onPathsChanged;
    private readonly Action _onDisplayChanged;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(AppServices app, Action onPathsChanged, Action onDisplayChanged)
    {
        _app = app;
        _onPathsChanged = onPathsChanged;
        _onDisplayChanged = onDisplayChanged;

        BrowseElinCommand = new RelayCommand(BrowseElin);
        BrowseWorkshopCommand = new RelayCommand(BrowseWorkshop);
        RedetectCommand = new RelayCommand(Redetect);
        OpenOverrideFolderCommand = new RelayCommand(() =>
            ShellService.OpenFolder(_app.Paths?.OverridePackageRoot));
        OpenElinFolderCommand = new RelayCommand(() => ShellService.OpenFolder(_app.Paths?.ElinRoot));
        OpenWorkshopFolderCommand = new RelayCommand(() => ShellService.OpenFolder(_app.Paths?.WorkshopRoot));
        OpenLogFolderCommand = new RelayCommand(() => ShellService.OpenFolder(AppPaths.LogDirectory));
        OpenBackupFolderCommand = new RelayCommand(() => ShellService.OpenFolder(AppPaths.BackupDirectory));
        OpenAppDataFolderCommand = new RelayCommand(() => ShellService.OpenFolder(AppPaths.Root));
        CreateOverridePackageCommand = new RelayCommand(CreateOverridePackage);
    }

    public RelayCommand BrowseElinCommand { get; }
    public RelayCommand BrowseWorkshopCommand { get; }
    public RelayCommand RedetectCommand { get; }
    public RelayCommand OpenOverrideFolderCommand { get; }
    public RelayCommand OpenElinFolderCommand { get; }
    public RelayCommand OpenWorkshopFolderCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }
    public RelayCommand OpenAppDataFolderCommand { get; }
    public RelayCommand CreateOverridePackageCommand { get; }

    public string ElinPath => _app.Paths?.ElinRoot ?? "not detected";
    public string WorkshopPath => _app.Paths?.WorkshopRoot ?? "not detected";
    public string OverridePath => _app.Paths?.OverridePackageRoot ?? "not available";
    public string LoadOrderPath => _app.Paths?.LoadOrderFile ?? "not found";
    public string LogPath => AppLog.LogFile ?? AppPaths.LogDirectory;
    public string AppDataPath => AppPaths.Root;

    public bool OverridePackageExists => _app.Paths is not null
                                         && Directory.Exists(_app.Paths.OverridePackageRoot);

    public bool AutoRefreshWorkshop
    {
        get => _app.Settings.AutoRefreshWorkshop;
        set { _app.Settings.AutoRefreshWorkshop = value; Persist(); OnPropertyChanged(); }
    }

    /// <summary>
    /// The only network access the application makes. Off leaves the News page showing
    /// whatever was cached by the last successful fetch.
    /// </summary>
    public bool EnableSteamNews
    {
        get => _app.Settings.EnableSteamNews;
        set { _app.Settings.EnableSteamNews = value; Persist(); OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether closing the window hides it to the notification area instead of quitting.
    /// Turning it off also removes the tray icon, since it would then do nothing.
    /// </summary>
    public bool MinimiseToTray
    {
        get => _app.Settings.MinimiseToTray;
        set
        {
            _app.Settings.MinimiseToTray = value;
            Persist();
            OnPropertyChanged();
            (System.Windows.Application.Current as App)?.ApplyTraySetting();
        }
    }

    /// <summary>
    /// Whether Mod Health may ask Steam what it currently publishes for the installed
    /// Workshop items. Off unless the user asks: it is the only thing that sends
    /// anything about their library, even though that is only public ID numbers.
    /// </summary>
    public bool EnableWorkshopChecks
    {
        get => _app.Settings.EnableWorkshopChecks;
        set { _app.Settings.EnableWorkshopChecks = value; Persist(); OnPropertyChanged(); }
    }

    /// <summary>
    /// Hand Workshop links to the Steam client rather than a browser. Local, and needs
    /// no sign-in - see the note beside it in the view.
    /// </summary>
    public bool OpenWorkshopInSteamApp
    {
        get => _app.Settings.OpenWorkshopInSteamApp;
        set { _app.Settings.OpenWorkshopInSteamApp = value; Persist(); OnPropertyChanged(); }
    }

    /// <summary>False when Steam is not installed, in which case the option cannot apply.</summary>
    public bool SteamClientInstalled => ShellService.IsSteamClientInstalled();

    public bool WatchFileChanges
    {
        get => _app.Settings.WatchFileChanges;
        set { _app.Settings.WatchFileChanges = value; Persist(); OnPropertyChanged(); }
    }

    public bool CreateLoadOrderBackups
    {
        get => _app.Settings.CreateLoadOrderBackups;
        set { _app.Settings.CreateLoadOrderBackups = value; Persist(); OnPropertyChanged(); }
    }

    public bool MirrorOverridesToUserFolder
    {
        get => _app.Settings.MirrorOverridesToUserFolder;
        set
        {
            _app.Settings.MirrorOverridesToUserFolder = value;
            if (_app.Overrides is not null) _app.Overrides.MirrorToUserFolder = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public string UserTextureFolder => _app.Paths?.UserTextureReplace ?? "not available";

    // ---- thumbnail size ----

    public bool ThumbSmall
    {
        get => _app.Settings.ThumbnailSize == ThumbnailSize.Small;
        set { if (value) SetThumb(ThumbnailSize.Small); }
    }

    public bool ThumbMedium
    {
        get => _app.Settings.ThumbnailSize == ThumbnailSize.Medium;
        set { if (value) SetThumb(ThumbnailSize.Medium); }
    }

    public bool ThumbLarge
    {
        get => _app.Settings.ThumbnailSize == ThumbnailSize.Large;
        set { if (value) SetThumb(ThumbnailSize.Large); }
    }

    private void SetThumb(ThumbnailSize size)
    {
        _app.Settings.ThumbnailSize = size;
        Persist();
        OnPropertyChanged(nameof(ThumbSmall));
        OnPropertyChanged(nameof(ThumbMedium));
        OnPropertyChanged(nameof(ThumbLarge));
        _onDisplayChanged();
    }

    // ---- load-order convention ----

    public bool LaterWins
    {
        get => _app.Settings.PriorityConvention == PriorityConvention.LaterWins;
        set { if (value) SetConvention(PriorityConvention.LaterWins); }
    }

    public bool EarlierWins
    {
        get => _app.Settings.PriorityConvention == PriorityConvention.EarlierWins;
        set { if (value) SetConvention(PriorityConvention.EarlierWins); }
    }

    private void SetConvention(PriorityConvention convention)
    {
        _app.Settings.PriorityConvention = convention;
        Persist();
        _app.RecomputeWinners();
        OnPropertyChanged(nameof(LaterWins));
        OnPropertyChanged(nameof(EarlierWins));
        _onDisplayChanged();
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private void Persist() => _app.SaveSettings();

    private void BrowseElin()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select your Elin installation folder (the one containing Elin.exe)",
            InitialDirectory = _app.Paths?.ElinRoot ?? string.Empty,
        };

        if (dialog.ShowDialog() != true) return;

        if (_app.SetElinPath(dialog.FolderName))
        {
            StatusMessage = "Elin folder updated. Refreshing...";
            RaisePathProperties();
            _onPathsChanged();
        }
        else
        {
            MessageBox.Show(
                "That folder does not look like an Elin installation.\n\n"
                + "Choose the folder that contains Elin.exe and Elin_Data.",
                "Not an Elin folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseWorkshop()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Elin Workshop content folder (…\\workshop\\content\\2135150)",
            InitialDirectory = _app.Paths?.WorkshopRoot ?? string.Empty,
        };

        if (dialog.ShowDialog() != true) return;

        _app.SetWorkshopPath(dialog.FolderName);
        StatusMessage = "Workshop folder updated. Refreshing...";
        RaisePathProperties();
        _onPathsChanged();
    }

    private void Redetect()
    {
        _app.Settings.ElinPath = null;
        _app.Settings.WorkshopPath = null;
        _app.ResolvePaths();

        StatusMessage = _app.IsConfigured
            ? $"Detected {_app.Paths!.ElinRoot}"
            : "Could not detect Elin automatically. Use Change to browse for it.";

        RaisePathProperties();
        _onPathsChanged();
    }

    private void CreateOverridePackage()
    {
        if (_app.Overrides is null) return;

        var result = _app.Overrides.EnsurePackage(_app.Settings.OverrideLoadPriority);
        StatusMessage = result.Message;
        OnPropertyChanged(nameof(OverridePackageExists));
    }

    public void RaisePathProperties()
    {
        OnPropertyChanged(nameof(ElinPath));
        OnPropertyChanged(nameof(WorkshopPath));
        OnPropertyChanged(nameof(OverridePath));
        OnPropertyChanged(nameof(LoadOrderPath));
        OnPropertyChanged(nameof(UserTextureFolder));
        OnPropertyChanged(nameof(OverridePackageExists));
    }
}
