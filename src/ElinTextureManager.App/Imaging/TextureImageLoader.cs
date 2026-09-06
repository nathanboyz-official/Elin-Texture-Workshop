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
///
/// Two things here exist purely for throughput, and both were measured against a real
/// library of ~2000 portraits:
///
///  - Decodes are gated. Every tile used to start its own Task.Run, each of which blocks
///    on file I/O. The thread pool injects new threads at roughly one per 500ms, so a
///    screenful of tiles spent most of its time waiting for threads rather than reading:
///    400 portraits took 41 SECONDS unbounded, against 0.1s gated to the core count and
///    0.7s done one at a time. Queueing is the whole cost, so the queue is bounded.
///
///  - The cache evicts. It used to stop accepting entries once full, which turned it into
///    dead weight the moment a library outgrew it - every miss re-read and re-decoded from
///    disk, for ever. It is now least-recently-used, bounded by decoded bytes rather than
///    by a count, because a 240x320 portrait costs 16x what a 128x128 sprite does.
/// </summary>
public static class TextureImageLoader
{
    /// <summary>
    /// Decoded-pixel budget for the cache. Bitmaps are frozen and shared, so this is the
    /// real memory cost; entries are dropped oldest-first once it is exceeded.
    /// </summary>
    private const long MaxCacheBytes = 192L * 1024 * 1024;

    /// <summary>A second bound, so thousands of tiny sprites cannot bloat the bookkeeping.</summary>
    private const int MaxCacheEntries = 4000;

    private sealed record CacheEntry(string Key, BitmapSource Image, long Bytes);

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> Map =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Most recently used at the front, so eviction takes from the back.</summary>
    private static readonly LinkedList<CacheEntry> Order = new();

    private static long _cachedBytes;

    /// <summary>
    /// Caps concurrent decodes. Blocking work items past this point would only queue on
    /// the thread pool anyway, and queue far worse than they do here.
    /// </summary>
    private static readonly SemaphoreSlim DecodeGate =
        new(Math.Max(2, Environment.ProcessorCount));

    /// <summary>
    /// Loads an image, optionally downscaling during decode for thumbnails.
    /// A decode width of 0 loads at full resolution.
    /// </summary>
    public static BitmapSource? Load(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var key = KeyFor(path, decodePixelWidth);
        if (TryGetCached(key, out var cached)) return cached;

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

            AddToCache(key, image);
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not load image {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads on a background thread, with the number of decodes in flight bounded.
    /// A cache hit skips the gate entirely, so scrolling back over seen tiles is free.
    /// </summary>
    public static async Task<BitmapSource?> LoadAsync(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (TryGetCached(KeyFor(path, decodePixelWidth), out var cached)) return cached;

        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(path, decodePixelWidth)).ConfigureAwait(false);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    /// <summary>
    /// Decides the decode width for a thumbnail. Sources smaller than the slot are decoded
    /// at full size so nearest-neighbour upscaling stays sharp.
    /// </summary>
    public static int DecodeWidthFor(int sourceWidth, int slotWidth)
    {
        if (sourceWidth <= 0) return 0;
        return sourceWidth > slotWidth * 2 ? slotWidth * 2 : 0;
    }

    // ---- cache ----

    private static string KeyFor(string path, int decodePixelWidth) =>
        decodePixelWidth > 0 ? $"{path}|{decodePixelWidth}" : path;

    private static bool TryGetCached(string key, out BitmapSource? image)
    {
        lock (CacheGate)
        {
            if (Map.TryGetValue(key, out var node))
            {
                // Touch it: this is what makes eviction least-recently-used.
                Order.Remove(node);
                Order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    private static void AddToCache(string key, BitmapSource image)
    {
        var bytes = EstimateBytes(image);

        lock (CacheGate)
        {
            // Two threads can decode the same file at once; first one in wins.
            if (Map.ContainsKey(key)) return;

            var node = Order.AddFirst(new CacheEntry(key, image, bytes));
            Map[key] = node;
            _cachedBytes += bytes;

            while ((_cachedBytes > MaxCacheBytes || Map.Count > MaxCacheEntries) && Order.Count > 1)
            {
                var oldest = Order.Last!;
                Order.RemoveLast();
                Map.Remove(oldest.Value.Key);
                _cachedBytes -= oldest.Value.Bytes;
            }
        }
    }

    /// <summary>Decoded size in memory, which is what the budget is really spending.</summary>
    private static long EstimateBytes(BitmapSource image)
    {
        try
        {
            var bytesPerPixel = Math.Max(1, (image.Format.BitsPerPixel + 7) / 8);
            return (long)image.PixelWidth * image.PixelHeight * bytesPerPixel;
        }
        catch
        {
            // Never let a bookkeeping failure stop an image being shown.
            return 64 * 1024;
        }
    }

    public static void Clear()
    {
        lock (CacheGate)
        {
            Map.Clear();
            Order.Clear();
            _cachedBytes = 0;
        }
    }

    /// <summary>Drops cached entries for one file, so a re-copied override refreshes.</summary>
    public static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (CacheGate)
        {
            // Every decode width of this file, hence the prefix match rather than a lookup.
            var stale = Map.Keys
                .Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in stale)
            {
                if (!Map.TryGetValue(key, out var node)) continue;
                Order.Remove(node);
                Map.Remove(key);
                _cachedBytes -= node.Value.Bytes;
            }
        }
    }
}
