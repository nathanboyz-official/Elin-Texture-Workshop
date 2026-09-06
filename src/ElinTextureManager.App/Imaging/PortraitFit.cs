using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Portraits;

namespace ElinTextureManager.App.Imaging;

/// <summary>
/// Puts a picture into the portrait frame without distorting it.
///
/// The picture is scaled by the same amount in both directions - whichever amount makes
/// it fit - and centred, so a wide picture ends up with empty strips above and below and
/// a tall one with strips at the sides. Stretching it to fill the frame instead would
/// make every face in the game slightly the wrong shape, which is worse than a border.
///
/// The strips are left transparent rather than filled, because a portrait is a PNG with
/// an alpha channel and the game draws whatever is behind it through them.
/// </summary>
public static class PortraitFit
{
    /// <summary>Whether a picture is already the right size, and so untouched.</summary>
    public static bool Fits(BitmapSource image) =>
        PortraitSize.IsStandard(image.PixelWidth, image.PixelHeight);

    /// <summary>
    /// The picture at portrait size. Returned unchanged when it is already that size, so
    /// a correctly made portrait is copied across byte for byte rather than redrawn.
    /// </summary>
    public static BitmapSource ToFrame(BitmapSource image) =>
        ToFrame(image, PortraitSize.Width, PortraitSize.Height);

    public static BitmapSource ToFrame(BitmapSource image, int width, int height)
    {
        if (image.PixelWidth == width && image.PixelHeight == height) return image;
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0) return image;

        var scale = Math.Min(
            width / (double)image.PixelWidth,
            height / (double)image.PixelHeight);

        var drawnWidth = Math.Max(1.0, image.PixelWidth * scale);
        var drawnHeight = Math.Max(1.0, image.PixelHeight * scale);

        var visual = new DrawingVisual();

        // Point sampling would throw away half the pixels of a photograph being scaled
        // down to 240 across; portraits are drawn art, not sprite sheets, so the smooth
        // filter is the right one here.
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(image, new Rect(
                (width - drawnWidth) / 2,
                (height - drawnHeight) / 2,
                drawnWidth,
                drawnHeight));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        // Back to straight alpha. Written premultiplied, every soft edge in the picture
        // darkens against the transparent border.
        var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        straight.Freeze();

        return straight;
    }
}
