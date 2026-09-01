namespace ElinTextureManager.Core.Model;

/// <summary>
/// Where a texture version comes from. Ordered loosely by "how much the user owns it".
/// Vanilla is reserved for a future vanilla-asset extractor; the data model supports it today.
/// </summary>
public enum TextureSourceType
{
    /// <summary>Extracted from the base game. Not populated in v1, but modelled.</summary>
    Vanilla = 0,
    /// <summary>A Steam Workshop item under steamapps/workshop/content/2135150.</summary>
    Workshop = 1,
    /// <summary>A hand-installed mod under Elin/Package.</summary>
    LocalMod = 2,
    /// <summary>A texture written by this application into its own override package.</summary>
    Override = 3,
}
