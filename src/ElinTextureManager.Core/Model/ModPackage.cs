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

    /// <summary>
    /// Raw &lt;tags&gt; values from package.xml, in the author's own spelling. These are the
    /// Workshop sections the mod was published under. Normalised by <see cref="ModSection"/>.
    /// </summary>
    public List<string> Tags { get; } = new();

    /// <summary>loadPriority from package.xml. Lower loads earlier (Elin Core is -100).</summary>
    /// <summary>
    /// The priority the game will actually use, clamped the way the game clamps it.
    /// </summary>
    public int? LoadPriority { get; set; }

    /// <summary>
    /// What package.xml literally said, before clamping. Kept because the difference is
    /// the whole point: a mod asking for 114514 believes it loads after everything, and
    /// in fact ties with every other mod that asked for too much.
    /// </summary>
    public string? DeclaredLoadPriority { get; set; }

    /// <summary>True when the game will not use the number this mod asked for.</summary>
    public bool LoadPriorityWasClamped =>
        int.TryParse(DeclaredLoadPriority?.Trim(), out var declared)
        && declared != Math.Clamp(declared, PackageLimits.MinLoadPriority, PackageLimits.MaxLoadPriority);

    /// <summary>
    /// True when package.xml gave something that is not a number, which the game's
    /// int.TryParse rejects - leaving the mod on the default without saying so.
    /// </summary>
    public bool LoadPriorityUnreadable =>
        !string.IsNullOrWhiteSpace(DeclaredLoadPriority)
        && !int.TryParse(DeclaredLoadPriority.Trim(), out _);

    public TextureSourceType SourceType { get; set; } = TextureSourceType.Workshop;
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>Local preview image (preview.jpg/png) if the mod ships one.</summary>
    public string? PreviewImagePath { get; set; }

    /// <summary>True when package.xml was missing or unreadable; the mod is still usable.</summary>
    public bool MetadataMissing { get; set; }

    public List<TextureFile> Textures { get; } = new();

    public bool HasTextureReplacements => Textures.Count > 0;
    public int TextureCount => Textures.Count;

    /// <summary>Files from the mod's "Texture Replace" folder.</summary>
    public int SpriteCount => Textures.Count(t => t.Kind == ReplacementKind.TextureReplace);

    /// <summary>Files from the mod's "Portrait" folder.</summary>
    public int PortraitCount => Textures.Count(t => t.Kind == ReplacementKind.Portrait);

    /// <summary>
    /// How many character sprites the mod replaces - objC / objCL / objCLL and friends.
    /// This is the "does this mod change how NPCs and monsters look" number, which is the
    /// hardest thing to tell from a Workshop listing alone.
    /// </summary>
    public int CharacterTextureCount => Textures.Count(t =>
        t.Kind == ReplacementKind.TextureReplace
        && !t.IsVariant
        && t.Category == TextureCategory.Characters);

    public bool ReplacesCharacters => CharacterTextureCount > 0;

    /// <summary>Position in loadorder.txt; -1 when the mod is not listed.</summary>
    public int LoadOrderIndex { get; set; } = -1;

    /// <summary>Enabled flag from loadorder.txt. Mods absent from the file are treated as enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when the mod is not listed in loadorder.txt (e.g. local Package mods).</summary>
    public bool InLoadOrderFile { get; set; }

    /// <summary>
    /// True when the mod's enabled state can be changed through loadorder.txt. Local
    /// packages under Elin\Package are always loaded and are not listed there.
    /// </summary>
    public bool CanToggle => SourceType == TextureSourceType.Workshop;

    public override string ToString() => $"{Name} ({Key})";
}
