using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.Controls;

/// <summary>
/// An HSV colour wheel: hue around the rim, saturation towards the centre, brightness on
/// a slider because a disc has only two dimensions to spend.
///
/// Drawn here rather than taken from a package. It is a bitmap and some trigonometry,
/// and a colour picker is not worth putting a third-party dependency in front of anyone
/// building this from source.
/// </summary>
public partial class ColourWheel : UserControl
{
    /// <summary>
    /// Pixels across the wheel bitmap. It is drawn once, shared by every instance and
    /// scaled by the layout, so this decides how smooth the gradient looks rather than
    /// how large the control is.
    /// </summary>
    private const int Resolution = 200;

    private static readonly BitmapSource Disc = BuildDisc();

    private double _hue;
    private double _saturation;
    private double _value = 0.7;
    private bool _updating;

    public ColourWheel()
    {
        InitializeComponent();

        WheelImage.Source = Disc;
        ValueSlider.Value = _value;

        // The colour is usually bound before the image has been given a size, and a
        // marker placed against a width of zero lands in the corner and stays there.
        Loaded += (_, _) => PlaceMarker();
        WheelImage.SizeChanged += (_, _) => PlaceMarker();
    }

    /// <summary>The chosen colour, as the six hex digits a style stores.</summary>
    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColourWheel),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexChanged));

    public string? Hex
    {
        get => (string?)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    private static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColourWheel wheel && !wheel._updating) wheel.ReadFromHex(e.NewValue as string);
    }

    /// <summary>Moves the wheel to show a colour that was set from outside.</summary>
    private void ReadFromHex(string? hex)
    {
        if (PccColour.FromHex(hex) is not { } rgb) return;

        var (hue, saturation, value) = PccColour.ToHsv(rgb.R, rgb.G, rgb.B);

        _hue = hue;
        _saturation = saturation;
        _value = value;

        _updating = true;
        ValueSlider.Value = value;
        _updating = false;

        PlaceMarker();
    }

    private void Publish()
    {
        var (r, g, b) = PccColour.FromHsv(_hue, _saturation, _value);

        _updating = true;
        Hex = PccColour.ToHex(r, g, b);
        _updating = false;
    }

    // ---- picking ----

    private void OnWheelDown(object sender, MouseButtonEventArgs e)
    {
        WheelImage.CaptureMouse();
        Pick(e.GetPosition(WheelImage));
    }

    private void OnWheelMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && WheelImage.IsMouseCaptured)
            Pick(e.GetPosition(WheelImage));
    }

    private void OnWheelUp(object sender, MouseButtonEventArgs e) => WheelImage.ReleaseMouseCapture();

    /// <summary>
    /// Turns a point on the disc into hue and saturation.
    ///
    /// Dragging past the rim keeps the hue and pins saturation at full, rather than
    /// stopping dead - a picker that only responds inside the circle feels broken the
    /// first time a drag overshoots.
    /// </summary>
    private void Pick(Point point)
    {
        var radius = WheelImage.ActualWidth / 2;
        if (radius <= 0) return;

        var dx = point.X - radius;
        var dy = radius - point.Y;

        var distance = Math.Sqrt(dx * dx + dy * dy);

        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp(distance / radius, 0, 1);

        PlaceMarker();
        Publish();
    }

    private void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _value = e.NewValue;
        if (!_updating) Publish();
    }

    private void PlaceMarker()
    {
        var radius = WheelImage.ActualWidth / 2;
        if (radius <= 0) return;

        var radians = _hue * Math.PI / 180;
        var x = radius + Math.Cos(radians) * _saturation * radius;
        var y = radius - Math.Sin(radians) * _saturation * radius;

        Place(Marker, x, y);
        Place(MarkerInner, x, y);
    }

    private static void Place(Shape shape, double x, double y)
    {
        Canvas.SetLeft(shape, x - shape.Width / 2);
        Canvas.SetTop(shape, y - shape.Height / 2);
    }

    /// <summary>
    /// Paints the disc once: angle is hue, distance from the centre is saturation.
    ///
    /// Drawn at full brightness. The brightness slider dims the chosen colour rather
    /// than the wheel, because a wheel that goes black as you lower brightness stops
    /// being usable exactly when you still need to pick a hue.
    /// </summary>
    private static BitmapSource BuildDisc()
    {
        var pixels = new byte[Resolution * Resolution * 4];
        var radius = Resolution / 2.0;

        for (var y = 0; y < Resolution; y++)
        for (var x = 0; x < Resolution; x++)
        {
            var dx = x - radius + 0.5;
            var dy = radius - y - 0.5;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var i = (y * Resolution + x) * 4;

            if (distance > radius)
            {
                pixels[i + 3] = 0;
                continue;
            }

            var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var (r, g, b) = PccColour.FromHsv(hue, distance / radius, 1);

            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;

            // One pixel of feathering at the rim, so the disc does not look sawn out.
            pixels[i + 3] = (byte)(distance > radius - 1 ? (radius - distance) * 255 : 255);
        }

        var bitmap = BitmapSource.Create(Resolution, Resolution, 96, 96,
            PixelFormats.Bgra32, null, pixels, Resolution * 4);

        bitmap.Freeze();
        return bitmap;
    }
}
