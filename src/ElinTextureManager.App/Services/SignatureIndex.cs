using ElinTextureManager.App.Imaging;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.App.Services;

/// <summary>Progress while the colour index is being built.</summary>
public sealed record IndexProgress(int Done, int Total)
{
    public double Fraction => Total == 0 ? 1 : Done / (double)Total;
}

/// <summary>
/// The colour index the reverse lookup searches.
///
/// Building it means decoding every image in the library, which takes about forty
/// seconds on a large one - far too long to do when someone pastes a screenshot. So it
/// is built once, written to the cache database, and afterwards only the files whose
/// size or timestamp changed are decoded again.
/// </summary>
public sealed class SignatureIndex
{
    /// <summary>
    /// Longest side an image is reduced to before its palette is counted. Small on
    /// purpose: a histogram does not get more truthful with more pixels, and this is
    /// the difference between a forty-second first run and a five-minute one.
    /// </summary>
    private const int DecodeSide = 96;

    private readonly CacheDatabase _cache;
    private readonly List<(string Path, SpriteSignature Signature)> _entries = new();

    public SignatureIndex(CacheDatabase cache) => _cache = cache;

    public IReadOnlyList<(string Path, SpriteSignature Signature)> Entries => _entries;

    public int Count => _entries.Count;

    /// <summary>True once there is something to search.</summary>
    public bool IsReady => _entries.Count > 0;

    /// <summary>
    /// Brings the index up to date with the given files, decoding only what the cache
    /// does not already hold a current signature for.
    /// </summary>
    public void Build(IEnumerable<TextureFile> files,
        IProgress<IndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        var wanted = files
            .GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var cached = _cache.LoadSignatures();

        var fresh = new List<(string Path, SpriteSignature Signature)>(wanted.Count);
        var stale = new List<TextureFile>();

        foreach (var file in wanted)
        {
            if (cached.TryGetValue(file.FullPath, out var hit)
                && hit.Size == file.FileSize
                && hit.ModifiedTicks == file.LastModifiedUtc.Ticks
                && SpriteSignature.FromBytes(hit.Blob) is { } signature)
            {
                fresh.Add((file.FullPath, signature));
            }
            else
            {
                stale.Add(file);
            }
        }

        AppLog.Info($"Colour index: {fresh.Count} cached, {stale.Count} to decode.");

        if (stale.Count > 0)
        {
            var built = new (string Path, SpriteSignature Signature, CachedSignature Row)?[stale.Count];
            var done = 0;

            Parallel.For(0, stale.Count,
                new ParallelOptions { CancellationToken = ct },
                i =>
                {
                    var file = stale[i];
                    var buffer = PixelDecoder.Decode(file.FullPath, DecodeSide);

                    // An image that will not decode still gets an entry, so the next run
                    // does not try again: an empty signature never matches anything.
                    var signature = buffer is null
                        ? new SpriteSignature
                        {
                            Histogram = new byte[SpriteSignature.BinCount],
                            SampleCount = 0,
                        }
                        : SpriteSignature.FromBgra(buffer.Bgra, buffer.Width, buffer.Height);

                    built[i] = (file.FullPath, signature,
                        new CachedSignature(file.FullPath, file.FileSize,
                            file.LastModifiedUtc.Ticks, signature.ToBytes()));

                    var count = Interlocked.Increment(ref done);
                    if (count % 64 == 0 || count == stale.Count)
                        progress?.Report(new IndexProgress(count, stale.Count));
                });

            var rows = new List<CachedSignature>(stale.Count);
            foreach (var entry in built)
            {
                if (entry is not { } e) continue;
                fresh.Add((e.Path, e.Signature));
                rows.Add(e.Row);
            }

            _cache.SaveSignatures(rows);
        }

        lock (_entries)
        {
            _entries.Clear();
            _entries.AddRange(fresh);
        }
    }

    public void Clear()
    {
        lock (_entries) _entries.Clear();
    }
}
