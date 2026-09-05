using System.Windows;
using System.Windows.Threading;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.App;

public partial class App : Application
{
    public AppServices Services { get; } = new();

    /// <summary>The notification-area icon, or null when it could not be created.</summary>
    public TrayIcon? Tray { get; private set; }

    /// <summary>
    /// Set when the application is genuinely going away, so the window knows the
    /// difference between the user closing it and the application shutting down.
    /// </summary>
    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The window can be hidden to the tray rather than open, and the default of
        // quitting when the last window closes would make that the same as quitting.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // An unhandled exception should surface as a message and a log entry, never as a
        // silent disappearance - this application touches the user's game folder.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Fatal error", args.ExceptionObject as Exception);

        Services.Initialize();

        var window = new MainWindow(Services);
        MainWindow = window;
        window.Show();

        SetUpTray(window);
    }

    /// <summary>
    /// Wires the tray icon to the window. Failing here must not stop the application
    /// starting: the icon is a convenience, and a machine where it cannot be created is
    /// still a machine where the rest of this works.
    /// </summary>
    private void SetUpTray(MainWindow window)
    {
        try
        {
            var tray = new TrayIcon();

            tray.OpenRequested += window.ShowFromTray;
            tray.SettingsRequested += window.ShowSettingsFromTray;
            tray.RestartRequested += Restart;
            tray.ExitRequested += Quit;
            tray.Visible = Services.Settings.MinimiseToTray;

            Tray = tray;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not create the notification-area icon", ex);
        }
    }

    /// <summary>
    /// Shuts down for real, from the tray menu.
    ///
    /// Goes through the window's close so that window size, settings and the scan are
    /// all put away exactly as they are when the user closes it themselves.
    /// </summary>
    public void Quit()
    {
        IsExiting = true;

        if (MainWindow is not null) MainWindow.Close();
        else Shutdown();
    }

    /// <summary>
    /// Closes and starts again.
    ///
    /// The new copy is launched from OnExit rather than here, so the settings, the
    /// selections and the cache database are all closed and written before anything
    /// reopens them. Two copies of this reading the same cache at once is how a cache
    /// gets corrupted, and that has already happened once.
    /// </summary>
    public void Restart()
    {
        _restartOnExit = true;
        Quit();
    }

    private bool _restartOnExit;

    /// <summary>Shows or hides the tray icon when the setting changes.</summary>
    public void ApplyTraySetting()
    {
        if (Tray is not null) Tray.Visible = Services.Settings.MinimiseToTray;
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
        Tray?.Dispose();
        Services.Dispose();

        if (_restartOnExit) StartAgain();

        base.OnExit(e);
    }

    /// <summary>
    /// Launches a fresh copy of this application, after everything this one held has been
    /// let go of.
    /// </summary>
    private static void StartAgain()
    {
        try
        {
            var exe = Environment.ProcessPath;

            if (string.IsNullOrEmpty(exe))
            {
                AppLog.Error("Cannot restart: the running program's own path is unknown.");
                return;
            }

            AppLog.Info($"Restarting: {exe}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            // Nothing useful can be shown here - the window is already gone.
            AppLog.Error("Could not start the application again", ex);
        }
    }
}
