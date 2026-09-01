namespace ElinTextureManager.Core.Model;

/// <summary>
/// A texture the user explicitly picked. Persisted so we can detect when Steam
/// updates the source mod underneath a selection.
/// </summary>
public sealed class OverrideSelection
{
    public required string TextureId { get; set; }

    /// <summary>File name actually written into the override package (e.g. objC_2115.png).</summary>
    public required string FileName { get; set; }

    public required string SourceModKey { get; set; }
    public string? SourceWorkshopId { get; set; }
    public required string SourceModName { get; set; }
    public required string SourcePath { get; set; }

    /// <summary>Hash of the source file at the moment it was selected.</summary>
    public string? SourceHash { get; set; }

    public TextureSourceType SourceType { get; set; } = TextureSourceType.Workshop;
    public DateTime SelectedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Profile this selection belongs to. Reserved for future profile support.</summary>
    public string Profile { get; set; } = "Default";
}
