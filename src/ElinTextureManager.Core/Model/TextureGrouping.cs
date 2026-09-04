namespace ElinTextureManager.Core.Model;

/// <summary>
/// Works out the groups a set of images should actually be offered in.
///
/// The raw prefix is a poor grouping on its own. A file called objC_2115.png yields
/// "objC" and half the library lands there, but a file called azurlane_USS_Honolulu.png
/// has no numeric tail to strip, so its prefix is the whole name and it becomes a group
/// of one. On a real library that produced 408 separate groups whose names all began
/// with "azurlane", 270 beginning with "gcdm", 126 with "yano" - a dropdown of a
/// thousand entries, almost none of which grouped anything.
///
/// So two rules. Names that share a leading word belong together, because that word is
/// how mod authors namespace their files. And a group holding a single image is not a
/// group; it goes to Other rather than taking up a line of its own.
/// </summary>
public static class TextureGrouping
{
    /// <summary>Below this, a group is not worth offering as a filter.</summary>
    public const int MinimumSize = 2;

    /// <summary>Where images that belong to no family end up.</summary>
    public const string Other = "Other";

    /// <summary>
    /// The family a prefix belongs to: everything up to the first separator.
    ///
    /// Only "_" and "#" count. A hyphen is part of the name in Elin's own conventions -
    /// "con-sleep" is one condition, not a "con" family - and splitting on it would
    /// invent groups nobody named.
    /// </summary>
    public static string FamilyOf(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Other;

        var trimmed = prefix.Trim();
        var cut = trimmed.IndexOfAny(new[] { '_', '#' });

        // Beginning with a separator means there is no leading word to group on, so it
        // goes to Other rather than becoming a group called "_something".
        if (cut == 0) return Other;

        return cut < 0 ? trimmed : trimmed[..cut];
    }

    /// <summary>
    /// Groups a set of entries, folding anything too small into <see cref="Other"/>.
    ///
    /// Scoped to the entries actually being shown: a family that is worth its own line
    /// across the whole library may be a single image once the page is filtered down to
    /// portraits, and offering it there would be a filter that empties the grid.
    /// </summary>
    public static GroupIndex Build(IEnumerable<TextureEntry> entries)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var family = FamilyOf(entry.Prefix);
            counts[family] = counts.GetValueOrDefault(family) + 1;
        }

        var kept = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var loose = 0;

        foreach (var (family, count) in counts)
        {
            if (count >= MinimumSize && !string.Equals(family, Other, StringComparison.OrdinalIgnoreCase))
                kept[family] = count;
            else
                loose += count;
        }

        if (loose > 0) kept[Other] = loose;

        return new GroupIndex(kept);
    }
}

/// <summary>The groups for one set of entries, and which one any entry belongs to.</summary>
public sealed class GroupIndex
{
    private readonly Dictionary<string, int> _counts;

    internal GroupIndex(Dictionary<string, int> counts) => _counts = counts;

    /// <summary>Groups by size, largest first, with Other last however big it is.</summary>
    public IReadOnlyList<(string Name, int Count)> Groups => _counts
        .Select(p => (Name: p.Key, Count: p.Value))
        .OrderBy(g => string.Equals(g.Name, TextureGrouping.Other, StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(g => g.Count)
        .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public int Count => _counts.Count;

    /// <summary>Which group an entry is shown under here.</summary>
    public string GroupOf(TextureEntry entry)
    {
        var family = TextureGrouping.FamilyOf(entry.Prefix);
        return _counts.ContainsKey(family) ? family : TextureGrouping.Other;
    }

    public bool Has(string? name) => name is not null && _counts.ContainsKey(name);
}
