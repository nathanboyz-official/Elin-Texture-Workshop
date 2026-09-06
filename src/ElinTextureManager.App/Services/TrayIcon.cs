using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ElinTextureManager.Core.Logging;
using Application = System.Windows.Application;

namespace ElinTextureManager.App.Services;

/// <summary>
/// The notification-area icon.
///
/// Closing the window hides it here rather than quitting, because this is a tool people
/// keep open beside the game and reach for a question at a time - and reopening it means
/// rescanning a library of eight thousand images to answer something they only just
/// thought of.
///
/// Built on the WinForms icon rather than a tray library from NuGet: it ships with the
/// runtime, so building this from source needs nothing anyone has to trust.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayIcon()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Elin Texture Workshop",
            Visible = false,
            ContextMenuStrip = BuildMenu(),
        };

        // Double click is the habit people have with tray icons, and it should do the
        // obvious thing rather than nothing.
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? RestartRequested;
    public event Action? ExitRequested;

    public bool Visible
    {
        get => _icon.Visible;
        set { if (!_disposed) _icon.Visible = value; }
    }

    /// <summary>
    /// Says where the window went, once.
    ///
    /// Without this, closing the window looks exactly like quitting, and the next thing
    /// the user does is start it again from the desktop - or conclude the close button
    /// is broken.
    /// </summary>
    public void ShowHiddenHint()
    {
        if (_disposed || !_icon.Visible) return;

        try
        {
            _icon.BalloonTipTitle = "Still running down here";
            _icon.BalloonTipText = "Elin Texture Workshop is in the notification area. "
                                   + "Double-click to open it, or right-click for options. "
                                   + "Turn this off in Settings if you would rather it quit.";
            _icon.BalloonTipIcon = ToolTipIcon.None;
            _icon.ShowBalloonTip(6000);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not show the tray hint", ex);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            // Matched to the application's own surface colours; the system default is a
            // white menu hanging off a dark window.
            BackColor = Color.FromArgb(28, 29, 33),
            ForeColor = Color.FromArgb(226, 224, 219),
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours()),
            ShowImageMargin = false,
        };

        menu.Items.Add(Item("Open", () => OpenRequested?.Invoke()));
        menu.Items.Add(Item("Settings", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Restart", () => RestartRequested?.Invoke()));
        menu.Items.Add(Item("Quit", () => ExitRequested?.Invoke()));

        return menu;
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// The window and taskbar icon, reused. Falls back to no icon rather than failing:
    /// a missing icon is a cosmetic problem, and throwing here would take down startup.
    /// </summary>
    private static Icon? LoadIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("AppIcon.ico", UriKind.Relative));
            return stream is null ? null : new Icon(stream.Stream);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not load the tray icon", ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Without this the icon stays in the tray as a ghost until something makes
        // Windows repaint that area.
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>Dark colours for the tray menu, so it does not arrive as a white rectangle.</summary>
internal sealed class DarkMenuColours : ProfessionalColorTable
{
    private static readonly Color Surface = Color.FromArgb(28, 29, 33);
    private static readonly Color Hover = Color.FromArgb(45, 47, 53);
    private static readonly Color Line = Color.FromArgb(58, 60, 67);

    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemBorder => Line;
    public override Color MenuBorder => Line;
    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
}
