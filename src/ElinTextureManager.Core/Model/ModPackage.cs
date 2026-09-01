namespace ElinTextureManager.Core.Model;

/// <summary>A mod discovered on disk (Workshop item or local Package folder).</summary>
public sealed class ModPackage
{
    /// <summary>Stable key: the Workshop ID for Workshop items, else the folder name.</summary>
    public required string Key { get; init; }

    /// <summary>Workshop ID when the mod lives under workshop/content/2135150, else null.</summary>
    public string? WorkshopId { get; init; }

    public required string Directory { get; init; }

    /// <summary>Title from package.xml, falling back to the folder name.</summary>
    public required string Name { get; set; }

    public string? ModId { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public bool Builtin { get; set; }

    /// <summary>loadPriority from package.xml. Lower loads earlier (Elin Core is -100).</summary>
    public int? LoadPriority { get; set; }

    public TextureSourceType SourceType { get; init; } = TextureSourceType.Workshop;
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>Local preview image (preview.jpg/png) if the mod ships one.</summary>
    public string? PreviewImagePath { get; set; }

    /// <summary>True when package.xml was missing or unreadable; the mod is still usable.</summary>
    public bool MetadataMissing { get; set; }

    public List<TextureFile> Textures { get; } = new();

    public bool HasTextureReplacements => Textures.Count > 0;
    public int TextureCount => Textures.Count;

    /// <summary>Position in loadorder.txt; -1 when the mod is not listed.</summary>
    public int LoadOrderIndex { get; set; } = -1;

    /// <summary>Enabled flag from loadorder.txt. Mods absent from the file are treated as enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when the mod is not listed in loadorder.txt (e.g. local Package mods).</summary>
    public bool InLoadOrderFile { get; set; }

    public override string ToString() => $"{Name} ({Key})";
}
