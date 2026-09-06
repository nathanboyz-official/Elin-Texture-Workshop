namespace ElinTextureManager.Core.Sheets;

/// <summary>
/// The tab names Elin loads a source sheet under.
///
/// The name of the tab is what decides whether the game reads a sheet at all - not the
/// file name, and not anything inside it. A tab called "Charas" or "chara sheet" is
/// simply never looked at, and nothing reports it: the mod loads, and the content is
/// missing.
///
/// Taken from the modding wiki's own list of supported SourceData and SourceLang.
/// </summary>
public static class SourceSheetNames
{
    /// <summary>Sheets holding game data - characters, items, zones and the rest.</summary>
    public static readonly IReadOnlySet<string> Data = new HashSet<string>(StringComparer.Ordinal)
    {
        "Chara", "CharaText", "Tactics", "Race", "Job", "Hobby",
        "Thing", "ThingV", "Food", "Recipe", "SpawnList", "Category",
        "Collectible", "KeyItem",
        "Element", "Calc", "Stat", "Check", "Faction", "Religion",
        "Zone", "ZoneAffix", "Quest", "Area", "HomeResource", "Research", "Person",
        "GlobalTile", "Block", "Floor", "Obj", "Deco", "CellEffect", "Material",
    };

    /// <summary>Sheets holding text.</summary>
    public static readonly IReadOnlySet<string> Lang = new HashSet<string>(StringComparer.Ordinal)
    {
        "General", "Game", "List", "Word", "Note",
    };

    public static bool IsData(string name) => Data.Contains(name);

    public static bool IsLang(string name) => Lang.Contains(name);

    public static bool IsKnown(string name) => IsData(name) || IsLang(name);

    /// <summary>
    /// A known name that differs only by case or spacing, when there is one. Almost every
    /// unrecognised tab is a near miss rather than something invented.
    /// </summary>
    public static string? NearMiss(string name)
    {
        var squashed = Squash(name);
        if (squashed.Length == 0) return null;

        foreach (var known in Data.Concat(Lang))
        {
            if (Squash(known) == squashed) return known;
        }

        return null;
    }

    private static string Squash(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Where the game's data rows begin. Rows 1 to 3 are the header, the column types and
    /// the defaults, copied from the official sheet.
    /// </summary>
    public const int FirstDataRow = 4;

    public const int HeaderRow = 1;
    public const int TypeRow = 2;
    public const int DefaultRow = 3;
}
