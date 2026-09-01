using System.Windows;
using System.Windows.Threading;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.App;

public partial class App : Application
{
    public AppServices Services { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // An unhandled exception should surface as a message and a log entry, never as a
        // silent disappearance - this application touches the user's game folder.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Fatal error", args.ExceptionObject as Exception);

        Services.Initialize();

        var window = new MainWindow(Services);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);

        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\n"
            + "The application will keep running. Details are in the log.",
            "Elin Texture Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("Application exiting.");
        Services.Dispose();
        base.OnExit(e);
    }
}
