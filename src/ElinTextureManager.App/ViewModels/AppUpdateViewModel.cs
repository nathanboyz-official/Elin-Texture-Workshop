using System.IO;
using System.Windows;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Services;
using ElinTextureManager.Core.Updates;

namespace ElinTextureManager.App.ViewModels;

/// <summary>
/// Keeps the application itself up to date from its GitHub releases.
///
/// One click downloads the new build, checks it against what GitHub publishes, closes,
/// copies it over this copy and opens it again. Nothing the user has made lives beside the
/// executable - settings, choices and the cache are in %APPDATA% - so nothing of theirs
/// is at stake.
/// </summary>
public sealed class AppUpdateViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly UpdateClient _client = new();

    private ReleaseInfo? _latest;
    private bool _bannerDismissed;
    private bool _isBusy;
    private string? _busyText;
    private double _progress;

    public AppUpdateViewModel(AppServices app)
    {
        _app = app;

        CheckCommand = new AsyncRelayCommand(() => CheckAsync(userRequested: true), () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && _latest is not null);
        ViewReleaseCommand = new RelayCommand(() => ShellService.OpenUrl(_latest?.PageUrl ?? UpdateClient.ReleasesPage));
        DismissCommand = new RelayCommand(() => { _bannerDismissed = true; Raise(); });
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand ViewReleaseCommand { get; }
    public RelayCommand DismissCommand { get; }

    public string CurrentVersionText =>"v" + UpdateClient.CurrentVersion.ToString(3);

    public bool IsUpdateAvailable => _latest is not null;

    /// <summary>The bar across the top: shown when there is something to install.</summary>
    public bool ShowBanner => (IsUpdateAvailable && !_bannerDismissed) || IsBusy;

    public string BannerText => IsBusy
        ? _busyText ?? "Updating..."
        : $"Elin Texture Workshop {LatestVersionText} is available. You have {CurrentVersionText}.";

    public string LatestVersionText => _latest is null ? "" : "v" + _latest.Version.ToString(3);

    public string UpdateButtonText => IsUpdateAvailable ? $"UPDATE TO {LatestVersionText}" : "";

    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); Raise(); }
    }

    /// <summary>Download progress, 0 to 100.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>Runs at startup when the setting allows it. Silent unless there is news.</summary>
    public Task CheckQuietlyAsync() =>
        _app.Settings.CheckForAppUpdates ? CheckAsync(userRequested: false) : Task.CompletedTask;

    private async Task CheckAsync(bool userRequested)
    {
        var result = await _client.CheckAsync(UpdateClient.CurrentVersion);

        if (result.Error is not null)
        {
            if (userRequested)
            {
                var open = MessageBox.Show(
                    $"Could not reach GitHub to check for updates:\n\n{result.Error}\n\n"
                    + "Open the download page in your browser instead?",
                    "Check for updates", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (open == MessageBoxResult.Yes) ShellService.OpenUrl(UpdateClient.ReleasesPage);
            }
            return;
        }

        _latest = result.IsNewer ? result.Latest : null;
        if (userRequested) _bannerDismissed = false;
        Raise();

        if (userRequested && !result.IsNewer)
        {
            MessageBox.Show($"You have the latest version, {CurrentVersionText}.",
                "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task InstallAsync()
    {
        var release = _latest;
        if (release is null) return;

        var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

        if (!UpdateInstaller.CanWriteTo(installDir))
        {
            var open = MessageBox.Show(
                $"This copy is in a folder Windows will not let it change:\n{installDir}\n\n"
                + "Download the new version from GitHub and extract it over this one, or move the "
                + "application to a folder of your own (Documents, Desktop) so it can update itself.\n\n"
                + "Open the download page?",
                "Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (open == MessageBoxResult.Yes) ShellService.OpenUrl(release.PageUrl);
            return;
        }

        var size = release.Size > 0 ? $" ({release.Size / 1048576.0:0} MB)" : "";
        var answer = MessageBox.Show(
            $"Update to {LatestVersionText}?\n\n"
            + $"The new version{size} is downloaded from GitHub, then this window closes and "
            + "reopens on the new version a few seconds later.\n\n"
            + "Your settings, texture choices and setups are kept.",
            "Update Elin Texture Workshop", MessageBoxButton.OKCancel, MessageBoxImage.Question,
            MessageBoxResult.OK);
        if (answer != MessageBoxResult.OK) return;

        IsBusy = true;
        try
        {
            var work = UpdateInstaller.WorkRoot;
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);

            var zip = Path.Combine(work, release.AssetName);
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                SetBusyText($"Downloading {LatestVersionText}... {p:P0}");
            });

            SetBusyText($"Downloading {LatestVersionText}...");
            await _client.DownloadAsync(release, zip, progress);

            SetBusyText("Unpacking...");
            var staged = await Task.Run(() => UpdateInstaller.Stage(zip, Path.Combine(work, "files")));
            var script = UpdateInstaller.WriteFinishScript(staged, installDir, Environment.ProcessId, work);

            SetBusyText($"Restarting on {LatestVersionText}...");
            AppLog.Info($"Updating {CurrentVersionText} -> {LatestVersionText}.");
            (Application.Current as App)?.FinishUpdate(script);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update failed", ex);
            IsBusy = false;

            var open = MessageBox.Show(
                $"The update could not be installed:\n\n{ex.Message}\n\n"
                + "Nothing was changed. Open the download page to update by hand?",
                "Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (open == MessageBoxResult.Yes) ShellService.OpenUrl(release.PageUrl);
        }
    }

    private void SetBusyText(string text)
    {
        _busyText = text;
        OnPropertyChanged(nameof(BannerText));
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(ShowBanner));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(IsIdle));
    }

    public bool IsIdle => !_isBusy;
}
