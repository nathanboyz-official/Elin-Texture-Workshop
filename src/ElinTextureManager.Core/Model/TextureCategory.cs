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
    public const string Other = "Other";

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
        new[] { Characters, Items, Portraits, Objects, Other };
}
