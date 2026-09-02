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

    public string Category => TextureCategory.ForPrefix(Prefix);

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

    public int SourceCount => Versions.Count;

    /// <summary>Distinct file hashes - two mods shipping the same PNG count once.</summary>
    public int UniqueImageCount => Versions
        .Select(v => v.Hash ?? v.FullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>A conflict is more than one mod supplying this texture ID.</summary>
    public bool HasConflict => Versions.Count > 1;

    /// <summary>True when every version is byte-identical - a conflict with no visual difference.</summary>
    public bool AllIdentical => Versions.Count > 1 && UniqueImageCount == 1;

    public string VersionSummary => UniqueImageCount == SourceCount
        ? $"{SourceCount} source{(SourceCount == 1 ? "" : "s")}"
        : $"{SourceCount} sources, {UniqueImageCount} unique";
}
