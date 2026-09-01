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

        _services.SaveSettings();
        _viewModel.Dispose();
    }
}
