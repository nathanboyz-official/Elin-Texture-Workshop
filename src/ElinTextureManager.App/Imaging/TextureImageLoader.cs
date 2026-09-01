using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.App.Imaging;

/// <summary>
/// Loads texture PNGs for display.
///
/// Three things matter for Elin sprites and all three are handled here:
///  - transparency is preserved (no background is baked in),
///  - the full image is decoded, sprite sheets included, never cropped,
///  - decoding happens off the UI thread and the result is frozen so it can be
///    handed to the dispatcher safely.
///
/// Files are opened, copied to memory and closed immediately, so the application never
/// holds a lock on a Workshop file that Steam might want to update.
/// </summary>
public static class TextureImageLoader
{
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bounded so a library of thousands of textures cannot exhaust memory.</summary>
    private const int MaxCacheEntries = 1200;

    /// <summary>
    /// Loads an image, optionally downscaling during decode for thumbnails.
    /// A decode width of 0 loads at full resolution.
    /// </summary>
    public static BitmapSource? Load(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var key = decodePixelWidth > 0 ? $"{path}|{decodePixelWidth}" : path;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            if (!File.Exists(path)) return null;

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            // Only downscale; upscaling here would throw away the pixel-art crispness
            // that nearest-neighbour rendering depends on.
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;

            image.EndInit();
            image.Freeze();

            if (Cache.Count < MaxCacheEntries) Cache[key] = image;
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not load image {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Loads on a background thread.</summary>
    public static Task<BitmapSource?> LoadAsync(string? path, int decodePixelWidth = 0) =>
        Task.Run(() => Load(path, decodePixelWidth));

    /// <summary>
    /// Decides the decode width for a thumbnail. Sources smaller than the slot are decoded
    /// at full size so nearest-neighbour upscaling stays sharp.
    /// </summary>
    public static int DecodeWidthFor(int sourceWidth, int slotWidth)
    {
        if (sourceWidth <= 0) return 0;
        return sourceWidth > slotWidth * 2 ? slotWidth * 2 : 0;
    }

    public static void Clear() => Cache.Clear();

    /// <summary>Drops cached entries for one file, so a re-copied override refreshes.</summary>
    public static void Invalidate(string path)
    {
        foreach (var key in Cache.Keys)
        {
            if (key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                Cache.TryRemove(key, out _);
        }
    }
}
