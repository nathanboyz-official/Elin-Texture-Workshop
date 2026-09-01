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
    public int ConflictCount => Index.Values.Count(e => e.HasConflict);

    public IEnumerable<TextureEntry> Conflicts =>
        Index.Values.Where(e => e.HasConflict);
}
