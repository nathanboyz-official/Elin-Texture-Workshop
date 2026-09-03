namespace ElinTextureManager.Core.Model;

/// <summary>
/// Which of Elin's replacement folders a file belongs to.
///
/// Elin packages mirror the layout of Package\_Elona, and several of those folders are
/// replacement points a mod uses:
///
///   "Texture Replace"  sprites addressed by a slot in the packed atlas (objC_2115.png)
///   "Portrait"         portraits addressed by their vanilla file name (UN_ashland.png)
///   "Actor\PCC"        the layered parts characters are built from - hair, clothes,
///                      body, face. Most character mods ship these and nothing else,
///                      which is why a library can look small and still change how
///                      every NPC looks.
///   "Texture"          whole loose images mirroring _Elona\Texture, which is where
///                      character packs put their drawn sprites.
///   "TextureforTE"     conditional variants for the TextureExpand mod, named with a
///                      condition suffix such as objC_600#hostility-enemy.png.
///
/// The distinction matters because an override has to be written back into the matching
/// folder of the override package, not just into "Texture Replace".
/// </summary>
public enum ReplacementKind
{
    TextureReplace = 0,
    Portrait = 1,
    Pcc = 2,
    Texture = 3,
    TextureExpand = 4,
}

public static class ReplacementKindExtensions
{
    /// <summary>
    /// Folder path inside a package for this kind, relative to the package root.
    /// PCC is nested, and the sub-folder under it (female/male) is part of the file's
    /// own relative path rather than of this root.
    /// </summary>
    public static string FolderName(this ReplacementKind kind) => kind switch
    {
        ReplacementKind.Portrait => "Portrait",
        ReplacementKind.Pcc => Path.Combine("Actor", "PCC"),
        ReplacementKind.Texture => "Texture",
        ReplacementKind.TextureExpand => "TextureforTE",
        _ => "Texture Replace",
    };

    /// <summary>The last path segment, which is what the folder search matches on.</summary>
    public static string LeafFolderName(this ReplacementKind kind) => kind switch
    {
        ReplacementKind.Pcc => "PCC",
        _ => kind.FolderName(),
    };

    public static string Label(this ReplacementKind kind) => kind switch
    {
        ReplacementKind.Portrait => "Portrait",
        ReplacementKind.Pcc => "PCC part",
        ReplacementKind.Texture => "Loose texture",
        ReplacementKind.TextureExpand => "TextureExpand variant",
        _ => "Texture",
    };

    /// <summary>Every kind the scanner looks for, in the order they are indexed.</summary>
    public static IReadOnlyList<ReplacementKind> All { get; } = new[]
    {
        ReplacementKind.TextureReplace,
        ReplacementKind.Portrait,
        ReplacementKind.Pcc,
        ReplacementKind.Texture,
        ReplacementKind.TextureExpand,
    };
}
