namespace ElinTextureManager.Core.Model;

/// <summary>
/// The index record for a single texture ID: every installed version of it.
/// This is the heart of the application.
/// </summary>
public sealed class TextureEntry
{
    public required string TextureId { get; init; }
    public required string Prefix { get; init; }
    public int? NumericId { get; init; }

    /// <summary>Which of Elin's replacement folders this ID belongs to.</summary>
    public ReplacementKind Kind { get; init; } = ReplacementKind.TextureReplace;

    /// <summary>The ID without its index namespace, for display. See TextureIdentity.</summary>
    public string DisplayId => TextureIdentity.Display(TextureId);

    /// <summary>
    /// Portraits are a category by virtue of the folder they live in; their prefix carries
    /// the group (Female, Male, Background …) instead.
    /// </summary>
    public string Category => TextureCategory.ForKind(Kind, Prefix);

    /// <summary>
    /// True for the "-overlay" layer that belongs on top of another portrait. Elin draws
    /// the two together, so an overlay is not a picture in its own right - it is shown
    /// inside its base portrait rather than as its own tile in the grid.
    /// </summary>
    public bool IsOverlay => Kind == ReplacementKind.Portrait
                             && PortraitGroup.IsOverlay(DisplayId);

    /// <summary>
    /// The overlay layer belonging to this portrait, linked during index construction.
    /// Null when the portrait has none.
    /// </summary>
    public TextureEntry? Overlay { get; set; }

    public bool HasOverlay => Overlay is not null;

    /// <summary>
    /// Set on an overlay once it has been attached to its base portrait. Only an attached
    /// overlay is hidden from the grid; a stray one stays visible, because a picture with
    /// nothing to attach it to still has to be reachable.
    /// </summary>
    public string? OverlayOwnerId { get; set; }

    public bool IsAttachedOverlay => OverlayOwnerId is not null;

    /// <summary>Active versions - files Elin actually loads, one per mod that ships it.</summary>
    public List<TextureFile> Versions { get; } = new();

    /// <summary>
    /// Alternates found in sub-folders of a mod's "Texture Replace" folder. Selectable,
    /// but not active in game, so they never inflate the conflict count.
    /// </summary>
    public List<TextureFile> Variants { get; } = new();

    /// <summary>Everything the comparison view can offer for this texture.</summary>
    public IEnumerable<TextureFile> AllSources => Versions.Concat(Variants);

    /// <summary>
    /// The base game's own copy of this image, when it ships as a loose file under
    /// Package\_Elona. Null for sprites packed into the Unity atlases.
    /// </summary>
    public TextureFile? Vanilla { get; set; }

    public bool HasVanilla => Vanilla is not null;

    /// <summary>Optional user-assigned or game-derived display name (e.g. "Gaki").</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The versions supplied by actual mods, which is the only thing a conflict can be
    /// between. Two other kinds of file live in <see cref="Versions"/> and neither is a
    /// competing mod:
    ///
    ///   * the base game's own file - it is what a mod replaces, not a rival to it;
    ///   * the copy this application writes into its own override package when you
    ///     choose a winner - counting that would mean resolving a conflict left it
    ///     still reported as one.
    ///
    /// Both stay in <see cref="Versions"/> because the winner resolver has to see them
    /// to work out what the game actually loads. They just do not count as sources.
    /// </summary>
    public IEnumerable<TextureFile> ModVersions => Versions.Where(v =>
        v.SourceType is not (TextureSourceType.Override or TextureSourceType.Vanilla));

    public int SourceCount => ModVersions.Count();

    /// <summary>Distinct file hashes - two mods shipping the same PNG count once.</summary>
    public int UniqueImageCount => ModVersions
        .Select(v => v.Hash ?? v.FullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>A conflict is more than one MOD supplying this texture ID.</summary>
    public bool HasConflict => SourceCount > 1;

    /// <summary>True when every version is byte-identical - a conflict with no visual difference.</summary>
    public bool AllIdentical => SourceCount > 1 && UniqueImageCount == 1;

    public string VersionSummary => UniqueImageCount == SourceCount
        ? $"{SourceCount} source{(SourceCount == 1 ? "" : "s")}"
        : $"{SourceCount} sources, {UniqueImageCount} unique";
}
