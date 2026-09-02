namespace ElinTextureManager.Core.Model;

/// <summary>
/// Which of Elin's replacement folders a file belongs to.
///
/// Elin packages mirror the layout of Package\_Elona, and two of those folders are
/// replacement points a texture mod uses:
///   "Texture Replace"  sprites addressed by atlas index (objC_2115.png)
///   "Portrait"         portraits addressed by their vanilla file name (UN_ashland.png)
///
/// The distinction matters because an override has to be written back into the matching
/// folder of the override package, not just into "Texture Replace".
/// </summary>
public enum ReplacementKind
{
    TextureReplace = 0,
    Portrait = 1,
}

public static class ReplacementKindExtensions
{
    /// <summary>Folder name inside a package for this kind.</summary>
    public static string FolderName(this ReplacementKind kind) => kind switch
    {
        ReplacementKind.Portrait => "Portrait",
        _ => "Texture Replace",
    };

    public static string Label(this ReplacementKind kind) => kind switch
    {
        ReplacementKind.Portrait => "Portrait",
        _ => "Texture",
    };
}
