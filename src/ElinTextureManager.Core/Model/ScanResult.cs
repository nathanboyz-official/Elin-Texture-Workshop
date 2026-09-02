namespace ElinTextureManager.Core.Model;

/// <summary>Everything one full scan produced.</summary>
public sealed class ScanResult
{
    public List<ModPackage> Mods { get; init; } = new();
    public Dictionary<string, TextureEntry> Index { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; init; } = new();

    public int ModCount => Mods.Count;
    public int TextureModCount => Mods.Count(m => m.HasTextureReplacements);
    public int TextureFileCount => Mods.Sum(m => m.TextureCount);
    public int UniqueTextureCount => Index.Count;

    public int ConflictCount => Conflicts.Count();

    /// <summary>
    /// The conflicts worth showing, defined once so the sidebar count and the Conflicts
    /// page can never disagree.
    ///
    /// A portrait overlay has no tile of its own, so a conflict on one is reported against
    /// the portrait it belongs to - otherwise two mods fighting over an overlay would be
    /// counted but unreachable.
    /// </summary>
    public IEnumerable<TextureEntry> Conflicts =>
        Index.Values.Where(IsConflict);

    /// <summary>Whether an entry counts as a conflict for display purposes.</summary>
    public static bool IsConflict(TextureEntry entry) =>
        !entry.IsAttachedOverlay
        && (entry.HasConflict || entry.Overlay?.HasConflict == true);
}
