using System.Windows;
using ElinTextureManager.App.Services;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _viewModel;

    public MainWindow(AppServices services)
    {
        _services = services;

        InitializeComponent();

        _viewModel = new MainViewModel(services, Dispatcher);
        _viewModel.SearchFocusRequested += () =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };

        DataContext = _viewModel;

        RestoreWindowState();

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closing += OnClosing;
    }

    private void RestoreWindowState()
    {
        var settings = _services.Settings;

        if (settings.WindowWidth > 400 && settings.WindowHeight > 300)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _services.Settings;

        settings.WindowMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }

        // Size and position are worth keeping either way, but the view model holds file
        // watchers and a scan: disposing it here would leave a hidden window wired to
        // nothing, so it only happens when the application is really going away.
        _services.SaveSettings();

        if (settings.MinimiseToTray && !App.IsExiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _viewModel.Dispose();

        // Shutdown is explicit because the window can be hidden rather than open; WPF
        // would otherwise quit the moment it goes away.
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Hides the window, telling the user where it went the first time.</summary>
    private void HideToTray()
    {
        Hide();

        if (_services.Settings.TrayHintShown) return;

        _services.Settings.TrayHintShown = true;
        _services.SaveSettings();

        (System.Windows.Application.Current as App)?.Tray?.ShowHiddenHint();
    }

    /// <summary>Brings the window back from the notification area.</summary>
    public void ShowFromTray()
    {
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = _services.Settings.WindowMaximized ? WindowState.Maximized : WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>Brings the window back and puts it on the Settings page.</summary>
    public void ShowSettingsFromTray()
    {
        ShowFromTray();
        _viewModel.Navigate("Settings");
    }
}
