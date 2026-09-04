using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.Services;

/// <summary>
/// Picks a colour from anywhere on the screen.
///
/// The screen is frozen the moment the picker opens: one snapshot is taken, shown
/// full-screen, and every colour is read out of it. The snapshot lives in memory until the
/// picker closes and is never written to disk or sent anywhere.
///
/// The earlier version instead laid a see-through window over every monitor and asked the
/// desktop for one pixel at a time. See-through windows are drawn by the processor rather
/// than the graphics card, so across two screens that was fifty megabytes of redrawing
/// forty times a second - the marker fell behind the cursor and clicks queued up behind
/// the redraw. Freezing the screen makes the window opaque, which the graphics card can
/// carry, and turns each colour lookup into a memory read.
/// </summary>
public static class ScreenColourPicker
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointI point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI
    {
        public int X;
        public int Y;
    }

    /// <summary>How many screen pixels across the magnifier shows. Odd, so one of them is
    /// the middle - that is the one being picked.</summary>
    private const int LoupePixels = 9;

    /// <summary>How big each of those pixels is drawn.</summary>
    private const double LoupeZoom = 16;

    private const double LoupeInner = LoupePixels * LoupeZoom;
    private const double LoupeSize = LoupeInner + 4;

    /// <summary>
    /// Lets the user click anywhere on any screen, and gives back what was under the
    /// cursor as six hex digits. Null if they pressed Escape or right-clicked.
    /// </summary>
    public static Task<string?> PickAsync(Window owner)
    {
        var result = new TaskCompletionSource<string?>();

        // Whole-desktop bounds in real pixels. Negative on a screen sitting above or to
        // the left of the main one, which is normal and handled throughout.
        var originX = (int)Math.Round(SystemParameters.VirtualScreenLeft);
        var originY = (int)Math.Round(SystemParameters.VirtualScreenTop);
        var width = (int)Math.Round(SystemParameters.VirtualScreenWidth);
        var height = (int)Math.Round(SystemParameters.VirtualScreenHeight);

        if (Snapshot(originX, originY, width, height) is not { } shot)
            return Task.FromResult<string?>(null);

        var (frozen, pixels, stride) = shot;

        (byte R, byte G, byte B)? ColourAt(int screenX, int screenY)
        {
            var x = screenX - originX;
            var y = screenY - originY;
            if (x < 0 || y < 0 || x >= width || y >= height) return null;

            var at = (y * stride) + (x * 4);
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }

        // The frozen screen, pixel for pixel under the real one.
        var backdrop = new Image
        {
            Source = frozen,
            Stretch = Stretch.Fill,
        };

        // A magnified window onto the same snapshot, so single pixels can be aimed at.
        // A cropped view costs nothing to make - it points at the snapshot rather than
        // copying out of it - so a fresh one per mouse move is cheaper than stretching
        // the whole desktop and clipping it.
        var loupeImage = new Image { Stretch = Stretch.Fill };

        RenderOptions.SetBitmapScalingMode(loupeImage, BitmapScalingMode.NearestNeighbor);

        var crosshair = new Border
        {
            Width = LoupeZoom,
            Height = LoupeZoom,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
        };

        var loupeCanvas = new Canvas { Width = LoupeInner, Height = LoupeInner };
        loupeCanvas.Children.Add(loupeImage);
        loupeCanvas.Children.Add(crosshair);

        Canvas.SetLeft(loupeImage, 0);
        Canvas.SetTop(loupeImage, 0);
        loupeImage.Width = LoupeInner;
        loupeImage.Height = LoupeInner;

        var loupe = new Border
        {
            Width = LoupeSize,
            Height = LoupeSize,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            ClipToBounds = true,
            Background = Brushes.Black,
            Child = loupeCanvas,
        };

        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
        };

        var label = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            Text = "picking",
        };

        var readout = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { swatch, label },
            },
        };

        var follower = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        follower.Children.Add(loupe);
        follower.Children.Add(readout);

        var canvas = new Canvas();
        canvas.Children.Add(backdrop);
        canvas.Children.Add(follower);

        Canvas.SetLeft(backdrop, 0);
        Canvas.SetTop(backdrop, 0);

        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Owner = owner,
            Cursor = Cursors.Cross,
            Background = Brushes.Black,
            Content = canvas,

            // Every monitor, so a colour can be taken from anywhere - including from the
            // game running on another screen.
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
        };

        backdrop.Width = overlay.Width;
        backdrop.Height = overlay.Height;

        string? current = null;
        var done = false;

        // Takes a point in the overlay's own units. The follower is placed with it
        // directly, and the snapshot is read at the matching real pixel - so nothing here
        // has to guess at a scaling factor.
        void MoveTo(Point local)
        {
            var screen = overlay.PointToScreen(local);
            var screenX = (int)Math.Round(screen.X);
            var screenY = (int)Math.Round(screen.Y);

            if (ColourAt(screenX, screenY) is { } rgb)
            {
                current = PccColour.ToHex(rgb.R, rgb.G, rgb.B);
                swatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
                label.Text = "#" + current;
            }

            // The magnified patch, kept inside the snapshot near an edge. The crosshair
            // moves off centre by however much the patch had to be pulled back, so it
            // stays over the pixel actually being read.
            var pixelX = Math.Clamp(screenX - originX, 0, width - 1);
            var pixelY = Math.Clamp(screenY - originY, 0, height - 1);

            var cropX = Math.Clamp(pixelX - (LoupePixels / 2), 0, Math.Max(0, width - LoupePixels));
            var cropY = Math.Clamp(pixelY - (LoupePixels / 2), 0, Math.Max(0, height - LoupePixels));

            loupeImage.Source = new CroppedBitmap(
                frozen,
                new Int32Rect(
                    cropX, cropY,
                    Math.Min(LoupePixels, width), Math.Min(LoupePixels, height)));

            Canvas.SetLeft(crosshair, (pixelX - cropX) * LoupeZoom);
            Canvas.SetTop(crosshair, (pixelY - cropY) * LoupeZoom);

            // Offset so the loupe never sits over the pixel being aimed at, and flipped
            // to the other side near an edge so it stays on screen.
            var x = local.X + 22;
            var y = local.Y + 22;

            if (x + LoupeSize + 8 > overlay.Width) x = local.X - LoupeSize - 22;
            if (y + LoupeSize + 44 > overlay.Height) y = local.Y - LoupeSize - 44;

            Canvas.SetLeft(follower, x);
            Canvas.SetTop(follower, y);
        }

        // Held so they can be taken off again. A picker that left handlers on the window
        // it belongs to would stack up another set every time it was opened.
        EventHandler? ownerClosed = null;
        ExitEventHandler? appExit = null;

        void Finish(string? hex)
        {
            if (done) return;
            done = true;

            if (ownerClosed is not null) owner.Closed -= ownerClosed;
            if (appExit is not null && Application.Current is { } running) running.Exit -= appExit;

            overlay.Close();
            result.TrySetResult(hex);
        }

        canvas.MouseMove += (_, e) => MoveTo(e.GetPosition(canvas));
        canvas.MouseLeftButtonDown += (_, e) => { MoveTo(e.GetPosition(canvas)); Finish(current); };
        canvas.MouseRightButtonDown += (_, _) => Finish(null);
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };

        // Whatever happens - the window closed by any route, the app shutting down, the
        // window it belongs to going away - the picker ends and the caller is answered.
        // The version before this one could be left behind: it tracked the cursor from a
        // timer, and closing the app did not stop the timer, so an invisible full-screen
        // window went on following the mouse and swallowing clicks.
        ownerClosed = (_, _) => Finish(null);
        appExit = (_, _) => Finish(null);

        overlay.Closed += (_, _) => Finish(null);
        owner.Closed += ownerClosed;
        if (Application.Current is { } app) app.Exit += appExit;

        // Deliberately not cancelled on deactivation. The overlay does not reliably win
        // activation on every machine, and cancelling when it never had focus made the
        // whole thing look broken - it would close before a click ever landed. Escape
        // and the right button are the ways out.
        overlay.Show();
        overlay.Activate();
        Keyboard.Focus(overlay);

        // The cursor may not move before the first click, so start it where it already is
        // rather than showing an empty readout in the corner.
        if (GetCursorPos(out var start))
            MoveTo(overlay.PointFromScreen(new Point(start.X, start.Y)));

        return result.Task;
    }

    /// <summary>
    /// One picture of the whole desktop, as something WPF can draw and as the raw bytes
    /// behind it.
    /// </summary>
    private static (BitmapSource Frozen, byte[] Pixels, int Stride)? Snapshot(
        int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        var stride = width * 4;
        var pixels = new byte[stride * (long)height];

        using var bitmap = new System.Drawing.Bitmap(
            width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var canvas = System.Drawing.Graphics.FromImage(bitmap))
        {
            canvas.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        }

        var locked = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    locked.Scan0 + (row * locked.Stride), pixels, row * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        var frozen = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        frozen.Freeze();

        return (frozen, pixels, stride);
    }
}
