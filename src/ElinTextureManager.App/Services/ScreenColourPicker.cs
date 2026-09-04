using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.Services;

/// <summary>
/// Picks a colour from anywhere on the screen.
///
/// Reads one pixel at a time from the desktop, under the cursor, and never takes or
/// keeps a picture of the screen: there is no screenshot in memory, nothing written to
/// disk and nothing sent anywhere. A transparent window over every monitor exists only
/// to catch the click and to show what is under the cursor while choosing.
/// </summary>
public static class ScreenColourPicker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointI point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI
    {
        public int X;
        public int Y;
    }

    /// <summary>The colour of the screen pixel at a point, in desktop coordinates.</summary>
    public static (byte R, byte G, byte B)? ColourAt(int x, int y)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        try
        {
            var value = GetPixel(dc, x, y);

            // CLR_INVALID: the point is off every screen.
            if (value == 0xFFFFFFFF) return null;

            return ((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    /// <summary>
    /// Lets the user click anywhere on any screen, and gives back what was under the
    /// cursor as six hex digits. Null if they pressed Escape or right-clicked.
    /// </summary>
    public static Task<string?> PickAsync(Window owner)
    {
        var result = new TaskCompletionSource<string?>();

        var swatch = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
        };

        var label = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(6, 3, 6, 3),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Text = "picking",
        };

        var follower = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        follower.Children.Add(swatch);
        follower.Children.Add(label);

        var canvas = new Canvas { Background = Brushes.Transparent };
        canvas.Children.Add(follower);

        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Owner = owner,
            Cursor = Cursors.Cross,
            Content = canvas,

            // Every monitor, so a colour can be taken from anywhere - including from the
            // game running on another screen.
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
        };

        string? current = null;
        var done = false;

        var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        follow.Tick += (_, _) =>
        {
            if (!GetCursorPos(out var point)) return;
            if (ColourAt(point.X, point.Y) is not { } rgb) return;

            current = PccColour.ToHex(rgb.R, rgb.G, rgb.B);

            swatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            label.Text = "#" + current;

            // The cursor is in physical pixels and the window is in WPF's own units.
            // On a scaled display those are different numbers, which is why the marker
            // used to drift further from the cursor the further across the screen it
            // went. The window's own transform is the only thing that knows the ratio.
            var scaled = new Point(point.X, point.Y);

            if (PresentationSource.FromVisual(overlay)?.CompositionTarget is { } target)
                scaled = target.TransformFromDevice.Transform(scaled);

            // Offset so the swatch never covers the pixel being sampled.
            Canvas.SetLeft(follower, scaled.X - overlay.Left + 20);
            Canvas.SetTop(follower, scaled.Y - overlay.Top + 20);
        };

        void Finish(string? hex)
        {
            if (done) return;
            done = true;

            follow.Stop();
            overlay.Close();
            result.TrySetResult(hex);
        }

        canvas.MouseLeftButtonDown += (_, _) => Finish(current);
        canvas.MouseRightButtonDown += (_, _) => Finish(null);
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };

        // Deliberately not cancelled on deactivation. The overlay does not reliably win
        // activation on every machine, and cancelling when it never had focus made the
        // whole thing look broken - it would close before a click ever landed. Escape
        // and the right button are the ways out.
        overlay.Show();
        overlay.Activate();
        Keyboard.Focus(overlay);

        follow.Start();

        return result.Task;
    }
}
