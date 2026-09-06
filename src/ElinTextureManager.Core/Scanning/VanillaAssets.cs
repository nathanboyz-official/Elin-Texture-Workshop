using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Scanning;

/// <summary>
/// The base game's own loose images, read from Elin\Package\_Elona.
///
/// This is where "what did it look like before any mod touched it?" is answered - but
/// only partly, and the limit is worth stating plainly:
///
///   * Portraits (Package\_Elona\Portrait) are individual PNGs named exactly as a mod
///     names its replacement, so the original is a direct file-name lookup.
///   * The same is true of the loose sheets and item textures under _Elona\Texture.
///   * "Texture Replace" sprites (objC_2115.png and friends) are NOT here. They address
///     a sprite by index inside a Unity sprite atlas packed into Elin_Data, and the
///     sprite rectangles are not a uniform grid that can be derived from the loose
///     objs_C.png - the indices used by installed mods run well past the number of
///     cells that file holds. Those originals need a Unity asset reader, which this
///     application deliberately does not carry. They are reported as unavailable
///     rather than guessed at.
///
/// Files here are only ever read.
/// </summary>
public sealed class VanillaAssets
{
    private readonly Dictionary<string, string> _byFileName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Root that was scanned, for display.</summary>
    public string? Root { get; private set; }

    public int Count => _byFileName.Count;

    /// <summary>
    /// Walks _Elona for loose images. Cheap: a few hundred files, and it never hashes
    /// or decodes them - dimensions are read from the PNG header on demand.
    /// </summary>
    public static VanillaAssets Load(ElinPaths? paths)
    {
        var assets = new VanillaAssets();
        if (paths is null) return assets;

        var root = paths.VanillaPackageRoot;
        assets.Root = root;

        if (!Directory.Exists(root))
        {
            AppLog.Warn($"Base game package not found at {root}; originals will be unavailable.");
            return assets;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!ImageInfo.IsSupported(file)) continue;

                var name = Path.GetFileName(file);

                // Two folders under _Elona can hold the same file name. First one wins and
                // the duplicate is logged rather than silently overwriting the original.
                if (assets._byFileName.TryAdd(name, file)) continue;
                AppLog.Warn($"Duplicate vanilla image name '{name}'; keeping the first found.");
            }

            AppLog.Info($"Vanilla assets indexed: {assets.Count} loose images under {root}.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not index the base game package at {root}", ex);
        }

        return assets;
    }

    /// <summary>The base game's copy of a file name, or null when it is not a loose file.</summary>
    public string? FindPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        return _byFileName.TryGetValue(fileName, out var path) && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Builds a <see cref="TextureFile"/> for the base game's copy, so the comparison view
    /// can treat it exactly like any other version. Returns null when there is no original.
    /// </summary>
    public TextureFile? BuildVanillaFile(TextureEntry entry)
    {
        var fileName = entry.Versions.Concat(entry.Variants).FirstOrDefault()?.FileName;
        var path = FindPath(fileName);
        if (path is null || fileName is null) return null;

        try
        {
            var fi = new FileInfo(path);

            var file = new TextureFile
            {
                FullPath = fi.FullName,
                FileName = fileName,
                Identity = entry.Kind == ReplacementKind.Portrait
                    ? TextureIdentity.ForPortrait(fileName)
                    : TextureIdentity.Parse(fileName),
                RelativePath = Root is null ? fi.Name : Path.GetRelativePath(Root, fi.FullName),
                ModKey = "_Elona",
                ModName = "Elin (base game)",
                SourceType = TextureSourceType.Vanilla,
                Kind = entry.Kind,
                FileSize = fi.Length,
                LastModifiedUtc = fi.LastWriteTimeUtc,
            };

            if (ImageInfo.TryReadPngSize(fi.FullName, out var w, out var h))
            {
                file.PixelWidth = w;
                file.PixelHeight = h;
            }

            // Hashed so "IDENTICAL TEXTURE" also works against the original - a mod that
            // ships the vanilla file unchanged is worth being told about.
            file.Hash = ImageInfo.TryComputeHash(fi.FullName);

            return file;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read the vanilla image at {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Attaches the base game's copy to every index entry that has one.</summary>
    public void AttachTo(ScanResult scan)
    {
        if (Count == 0) return;

        var attached = 0;
        foreach (var entry in scan.Index.Values)
        {
            entry.Vanilla = BuildVanillaFile(entry);
            if (entry.Vanilla is not null) attached++;
        }

        AppLog.Info($"Originals available for {attached} of {scan.Index.Count} replaced images.");
    }
}
