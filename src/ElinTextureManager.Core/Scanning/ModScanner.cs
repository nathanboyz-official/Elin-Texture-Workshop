using System.Collections.Concurrent;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.Core.Scanning;

public sealed record ScanProgress(string Stage, int Done, int Total)
{
    public double Fraction => Total <= 0 ? 0 : (double)Done / Total;
}

public sealed class ScanOptions
{
    /// <summary>Hashing enables identical-texture detection and source-update detection.</summary>
    public bool ComputeHashes { get; set; } = true;

    /// <summary>How deep below a mod root to look for a "Texture Replace" folder.</summary>
    public int MaxSearchDepth { get; set; } = 4;

    /// <summary>Also scan Elin\Package for hand-installed mods.</summary>
    public bool IncludeLocalPackages { get; set; } = true;

    /// <summary>
    /// Which replacement folders to index besides "Texture Replace". Elin packages
    /// mirror the layout of Package\_Elona, and a mod that only ships PCC parts or loose
    /// Texture files is otherwise completely invisible - on a real library that is more
    /// images than the ones which do show up.
    /// </summary>
    public HashSet<ReplacementKind> Kinds { get; } = new(ReplacementKindExtensions.All);

    public bool Includes(ReplacementKind kind) => Kinds.Contains(kind);

    /// <summary>Kept for callers that only care about portraits.</summary>
    public bool IncludePortraits
    {
        get => Includes(ReplacementKind.Portrait);
        set { if (value) Kinds.Add(ReplacementKind.Portrait); else Kinds.Remove(ReplacementKind.Portrait); }
    }
}

/// <summary>
/// Walks Workshop items and local packages and produces the texture index.
/// All I/O happens off the UI thread; the scanner itself is UI-agnostic.
/// </summary>
public sealed class ModScanner
{
    private readonly ScanOptions _options;
    private readonly CacheDatabase? _cache;

    /// <summary>Cache snapshot for the scan in progress; empty when caching is off.</summary>
    private Dictionary<string, CachedTexture> _cached = new(StringComparer.OrdinalIgnoreCase);

    public ModScanner(ScanOptions? options = null, CacheDatabase? cache = null)
    {
        _options = options ?? new ScanOptions();
        _cache = cache;
    }

    public async Task<ScanResult> ScanAsync(
        ElinPaths paths,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() => Scan(paths, progress, ct), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a scan on the calling thread. Used by tests and by tooling; the UI always
    /// goes through <see cref="ScanAsync"/> so disk work stays off the dispatcher.
    /// </summary>
    public ScanResult ScanSynchronously(
        ElinPaths paths,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
        => Scan(paths, progress, ct);

    private ScanResult Scan(ElinPaths paths, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var result = new ScanResult();
        var modDirs = new List<(string dir, TextureSourceType type)>();

        _cached = _cache?.LoadAll() ?? new Dictionary<string, CachedTexture>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(paths.WorkshopRoot) && Directory.Exists(paths.WorkshopRoot))
        {
            foreach (var d in SafeEnumerateDirectories(paths.WorkshopRoot, result))
                modDirs.Add((d, TextureSourceType.Workshop));
        }
        else
        {
            result.Errors.Add("Workshop folder not found - only local packages will be scanned.");
            AppLog.Warn("Workshop root missing or unset during scan.");
        }

        if (_options.IncludeLocalPackages && Directory.Exists(paths.PackageRoot))
        {
            foreach (var d in SafeEnumerateDirectories(paths.PackageRoot, result))
            {
                var isOverride = string.Equals(Path.GetFileName(d), ElinPaths.OverridePackageName,
                    StringComparison.OrdinalIgnoreCase);
                modDirs.Add((d, isOverride ? TextureSourceType.Override : TextureSourceType.LocalMod));
            }
        }

        // Elin's own Custom folder. Not a mod, but laid out like one - Custom\Portrait
        // sits where a mod's Portrait folder would - so the same walk finds it. Without
        // this a portrait the user added themselves is a file they cannot see anywhere
        // in this application, which is a poor answer to "did that work?".
        if (Directory.Exists(paths.CustomRoot))
            modDirs.Add((paths.CustomRoot, TextureSourceType.Custom));

        AppLog.Info($"Scanning {modDirs.Count} mod folders.");
        progress?.Report(new ScanProgress("Scanning mods", 0, modDirs.Count));

        var bag = new ConcurrentBag<ModPackage>();
        var done = 0;

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1),
        };

        try
        {
            Parallel.ForEach(modDirs, parallelOptions, item =>
            {
                try
                {
                    var mod = ScanMod(item.dir, item.type, result);
                    if (mod is not null) bag.Add(mod);
                }
                catch (Exception ex)
                {
                    // One malformed mod must never abort the scan.
                    RecordError(result, $"Failed to scan {item.dir}: {ex.Message}");
                }
                finally
                {
                    var n = Interlocked.Increment(ref done);
                    if (n % 5 == 0 || n == modDirs.Count)
                        progress?.Report(new ScanProgress("Scanning mods", n, modDirs.Count));
                }
            });
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Scan cancelled.");
            throw;
        }

        result.Mods.AddRange(bag.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase));

        progress?.Report(new ScanProgress("Building index", 0, 1));
        TextureIndexBuilder.Build(result);
        UpdateCache(result);

        AppLog.Info($"Scan complete: {result.ModCount} mods, {result.TextureModCount} texture mods, "
                    + $"{result.TextureFileCount} textures, {result.UniqueTextureCount} unique IDs, "
                    + $"{result.ConflictCount} conflicts, {result.Errors.Count} errors.");

        progress?.Report(new ScanProgress("Done", 1, 1));
        return result;
    }

    /// <summary>Writes what this scan learned back to the cache and drops vanished files.</summary>
    private void UpdateCache(ScanResult result)
    {
        if (_cache is null || !_cache.IsOpen) return;

        try
        {
            var all = result.Mods.SelectMany(m => m.Textures).ToList();

            _cache.SaveAll(all.Select(t => new CachedTexture(
                t.FullPath, t.FileSize, t.LastModifiedUtc.Ticks,
                t.PixelWidth, t.PixelHeight, t.Hash)));

            _cache.PruneMissing(all.Select(t => t.FullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Cache update skipped: {ex.Message}");
        }
    }

    private ModPackage? ScanMod(string dir, TextureSourceType type, ScanResult result)
    {
        var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) return null;

        var isWorkshop = type == TextureSourceType.Workshop;
        var workshopId = isWorkshop && folderName.All(char.IsDigit) ? folderName : null;

        var mod = new ModPackage
        {
            Key = workshopId ?? folderName,
            WorkshopId = workshopId,
            Directory = dir,
            Name = folderName,
            SourceType = type,
        };

        // "Custom" is the folder's name, not a description of what is in it.
        if (type == TextureSourceType.Custom) mod.Name = "Added by you";

        try { mod.LastModifiedUtc = Directory.GetLastWriteTimeUtc(dir); }
        catch { }

        PackageMetadataParser.Apply(mod, Path.Combine(dir, "package.xml"));
        mod.PreviewImagePath = FindPreviewImage(dir);

        // The packages shipped with the game (_Elona, _ModdingKit, ...) declare
        // builtin=true. Their images ARE the originals, not replacements of anything, so
        // indexing them as versions would turn every replaced portrait into a false
        // conflict. They are surfaced through VanillaAssets instead.
        if (mod.Builtin)
        {
            mod.SourceType = TextureSourceType.Vanilla;
            return mod;
        }

        foreach (var textureRoot in FindReplacementFolders(dir, ElinPaths.TextureReplaceFolder, result))
            CollectTextures(mod, textureRoot, result);

        // The other replacement folders hold whole files addressed by name, so they are
        // walked recursively and each file keeps its path below the root. PCC needs that:
        // "female\pcc_hair_x.png" and "male\pcc_hair_x.png" are different images.
        foreach (var kind in ReplacementKindExtensions.All)
        {
            if (kind == ReplacementKind.TextureReplace) continue;
            if (!_options.Includes(kind)) continue;

            foreach (var root in FindReplacementFolders(dir, kind.LeafFolderName(), result))
                CollectNamed(mod, root, kind, result);
        }

        return mod;
    }

    private static string? FindPreviewImage(string dir)
    {
        foreach (var name in new[] { "preview.jpg", "preview.png", "preview.jpeg", "thumbnail.png" })
        {
            var p = Path.Combine(dir, name);
            try { if (File.Exists(p)) return p; }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Finds a named replacement folder ("Texture Replace" or "Portrait"). In every mod
    /// observed they sit directly under the mod root, but the search is depth-limited
    /// rather than fixed so unusual layouts still work.
    /// </summary>
    private IEnumerable<string> FindReplacementFolders(string root, string wanted, ScanResult result)
    {
        var found = new List<string>();
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > _options.MaxSearchDepth) continue;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception ex)
            {
                RecordError(result, $"Cannot list {dir}: {ex.Message}");
                continue;
            }

            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);

                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(sub);
                    continue; // Do not descend further; its children are variants.
                }

                // Never look for one replacement folder inside the other.
                if (IsReplacementFolderName(name)) continue;

                if (IsReparsePoint(sub)) continue;
                queue.Enqueue((sub, depth + 1));
            }
        }

        return found;
    }

    /// <summary>
    /// True for any folder that is itself a replacement point. The search never descends
    /// into one looking for another, so a stray "Texture" folder inside "Texture Replace"
    /// cannot pull the same files in twice under two different kinds.
    /// </summary>
    private static bool IsReplacementFolderName(string name) =>
        ReplacementKindExtensions.All.Any(k =>
            string.Equals(name, k.LeafFolderName(), StringComparison.OrdinalIgnoreCase));

    private static bool IsReparsePoint(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch { return true; }
    }

    private void CollectTextures(ModPackage mod, string textureRoot, ScanResult result)
    {
        // Active files: directly inside "Texture Replace".
        AddFiles(mod, textureRoot, ReplacementKind.TextureReplace,
            isVariant: false, variantName: null, result);

        // Variants: one level of sub-folders (e.g. "unused", "1_Regular_Tights").
        string[] subs;
        try { subs = Directory.GetDirectories(textureRoot); }
        catch { return; }

        foreach (var sub in subs)
        {
            if (IsReparsePoint(sub)) continue;
            AddFiles(mod, sub, ReplacementKind.TextureReplace,
                isVariant: true, variantName: Path.GetFileName(sub), result);
        }
    }

    /// <summary>
    /// Indexes a replacement folder whose files are addressed by name rather than by an
    /// atlas slot: Portrait, Actor\PCC, Texture and TextureforTE.
    ///
    /// Walked recursively, because PCC splits by sex and mods nest their loose textures.
    /// Each file keeps its path below the root, which is what stops "female\pcc_hair_x"
    /// and "male\pcc_hair_x" from collapsing into one entry.
    /// </summary>
    private void CollectNamed(ModPackage mod, string root, ReplacementKind kind, ScanResult result)
    {
        AddFiles(mod, root, kind, isVariant: false, variantName: null, result, root);

        string[] subs;
        try { subs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
        catch (Exception ex)
        {
            RecordError(result, $"Cannot list {root}: {ex.Message}");
            return;
        }

        foreach (var sub in subs)
        {
            if (IsReparsePoint(sub)) continue;
            AddFiles(mod, sub, kind, isVariant: false, variantName: null, result, root);
        }
    }

    private void AddFiles(ModPackage mod, string dir, ReplacementKind kind,
        bool isVariant, string? variantName, ScanResult result, string? replacementRoot = null)
    {
        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch (Exception ex)
        {
            RecordError(result, $"Cannot read {dir}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            if (!ImageInfo.IsSupported(file)) continue;

            try
            {
                var fi = new FileInfo(file);
                var name = fi.Name;

                // Relative to the replacement folder, so nesting survives into the ID.
                var withinRoot = replacementRoot is null
                    ? name
                    : Relative(replacementRoot, fi.FullName);

                var texture = new TextureFile
                {
                    FullPath = fi.FullName,
                    FileName = name,
                    Identity = TextureIdentity.ForReplacement(kind, withinRoot),
                    RelativePath = Relative(mod.Directory, fi.FullName),
                    ModKey = mod.Key,
                    ModName = mod.Name,
                    WorkshopId = mod.WorkshopId,
                    SourceType = mod.SourceType,
                    Kind = kind,
                    IsVariant = isVariant,
                    VariantName = variantName,
                    FileSize = fi.Length,
                    LastModifiedUtc = fi.LastWriteTimeUtc,
                };

                if (string.IsNullOrEmpty(texture.TextureId))
                {
                    RecordError(result, $"Skipping unnamed texture file: {fi.FullName}");
                    continue;
                }

                // A cache entry is only trusted when size and modified time both match,
                // so a Steam update always invalidates it.
                if (_cached.TryGetValue(fi.FullName, out var hit)
                    && hit.Size == fi.Length
                    && hit.ModifiedTicks == fi.LastWriteTimeUtc.Ticks
                    && (!_options.ComputeHashes || hit.Hash is not null))
                {
                    texture.PixelWidth = hit.Width;
                    texture.PixelHeight = hit.Height;
                    texture.Hash = hit.Hash;
                }
                else
                {
                    if (ImageInfo.TryReadPngSize(fi.FullName, out var w, out var h))
                    {
                        texture.PixelWidth = w;
                        texture.PixelHeight = h;
                    }
                    else
                    {
                        RecordError(result, $"Unreadable or invalid PNG: {fi.FullName}");
                    }

                    if (_options.ComputeHashes)
                        texture.Hash = ImageInfo.TryComputeHash(fi.FullName);
                }

                lock (mod.Textures) mod.Textures.Add(texture);
            }
            catch (Exception ex)
            {
                RecordError(result, $"Failed on {file}: {ex.Message}");
            }
        }
    }

    private static string Relative(string root, string full)
    {
        try { return Path.GetRelativePath(root, full); }
        catch { return full; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, ScanResult result)
    {
        try { return Directory.GetDirectories(root); }
        catch (Exception ex)
        {
            RecordError(result, $"Cannot enumerate {root}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static void RecordError(ScanResult result, string message)
    {
        AppLog.Warn(message);
        lock (result.Errors)
        {
            // Keep the list bounded; the log file has the full history.
            if (result.Errors.Count < 500) result.Errors.Add(message);
        }
    }
}
