namespace ElinTextureManager.Core.Model;

/// <summary>
/// One replacement image found inside a mod's "Texture Replace" or "Portrait" folder.
/// </summary>
public sealed class TextureFile
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required TextureIdentity Identity { get; init; }

    /// <summary>Path of the file relative to its mod folder, for display.</summary>
    public required string RelativePath { get; init; }

    public required string ModKey { get; init; }
    public required string ModName { get; set; }
    public string? WorkshopId { get; init; }
    public TextureSourceType SourceType { get; init; }

    /// <summary>
    /// Which replacement folder the file came from. This decides where an override for it
    /// has to be written, so it travels with the file rather than being re-derived.
    /// </summary>
    public ReplacementKind Kind { get; init; } = ReplacementKind.TextureReplace;

    /// <summary>
    /// False when the file sits in a sub-folder of "Texture Replace" (e.g. "unused",
    /// "1_Regular_Tights"). Elin loads the files directly inside the folder, so variants
    /// are offered as selectable alternatives but never counted as active conflicts.
    /// </summary>
    public bool IsVariant { get; init; }

    /// <summary>Sub-folder name for a variant, used as its label in the UI.</summary>
    public string? VariantName { get; init; }

    public long FileSize { get; set; }
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>SHA-256 of the file bytes; null until hashing runs.</summary>
    public string? Hash { get; set; }

    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }

    public string TextureId => Identity.TextureId;
    public string Prefix => Identity.Prefix;

    /// <summary>
    /// Portraits are a category by virtue of the folder they live in; their prefix carries
    /// the group instead, so it must not be asked what category they are.
    /// </summary>
    public string Category => TextureCategory.ForKind(Kind, Identity.Prefix);

    /// <summary>
    /// The ID as the user should read it. Portrait IDs are namespaced in the index so a
    /// portrait and a sprite that happen to share a file name stay separate entries;
    /// the namespace is an implementation detail and never shown.
    /// </summary>
    public string DisplayId => TextureIdentity.Display(Identity.TextureId);

    public string DimensionsText => PixelWidth > 0 && PixelHeight > 0
        ? $"{PixelWidth} x {PixelHeight}"
        : "unknown";
}
