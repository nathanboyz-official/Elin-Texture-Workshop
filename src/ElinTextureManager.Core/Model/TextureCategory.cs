namespace ElinTextureManager.Core.Model;

/// <summary>
/// Maps a texture prefix to a friendly grouping for the sidebar.
/// Unknown prefixes are deliberately NOT discarded - they surface under <see cref="Other"/>
/// and remain filterable by their raw prefix.
/// </summary>
public static class TextureCategory
{
    public const string Characters = "Characters";
    public const string Items = "Items";
    public const string Portraits = "Portraits";
    public const string Objects = "Objects";

    /// <summary>The layered parts characters are built from. See PccPart.</summary>
    public const string Pcc = "PCC";

    public const string Other = "Other";

    /// <summary>
    /// The category for a file, given the folder it came from. The folder decides it for
    /// everything except "Texture Replace", where only the file name carries the meaning.
    /// </summary>
    public static string ForKind(ReplacementKind kind, string? prefix) => kind switch
    {
        ReplacementKind.Portrait => Portraits,
        ReplacementKind.Pcc => Pcc,
        _ => ForPrefix(prefix),
    };

    private static readonly Dictionary<string, string> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["objC"] = Characters,
            ["objCL"] = Characters,
            ["objCLL"] = Characters,
            ["chara"] = Characters,
            ["pcc"] = Characters,
            ["obj"] = Items,
            ["objS"] = Objects,
            ["objL"] = Objects,
            ["objs"] = Objects,
            ["portrait"] = Portraits,
            ["face"] = Portraits,
            ["pc"] = Portraits,
        };

    public static string ForPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Other;
        return Known.TryGetValue(prefix, out var c) ? c : Other;
    }

    /// <summary>Categories shown as fixed sidebar entries, in display order.</summary>
    public static IReadOnlyList<string> Primary { get; } =
        new[] { Characters, Items, Portraits, Pcc, Objects, Other };
}
