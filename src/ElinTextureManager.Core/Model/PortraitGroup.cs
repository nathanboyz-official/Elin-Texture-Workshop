namespace ElinTextureManager.Core.Model;

/// <summary>
/// Sorts a portrait file name into a group, because "Portraits" alone is 2000 entries.
///
/// Elin's portrait names carry their own structure, confirmed against the 473 files the
/// game ships and every portrait the installed mods add:
///
///   c_f-1          a generic female portrait
///   c_m-12         a generic male one
///   special_n-yeek "n" for the ones with no gender - slimes, animals, machines
///   UN_ashland     a named character
///   BG_3, BGF_1    a background, not a character at all
///   c_f-1-overlay  the overlay layer that belongs on top of c_f-1
///
/// The gender letter is a whole segment: an underscore, then f/m/n, then a separator.
/// Matching it as a segment is what keeps "UN_azurlane_IJN_..." out of the neutral group.
/// </summary>
public static class PortraitGroup
{
    public const string Female = "Female";
    public const string Male = "Male";
    public const string Neutral = "Neutral";
    public const string Named = "Named";
    public const string Background = "Background";
    public const string Other = "Other";

    /// <summary>Suffix Elin uses for the layer drawn on top of a portrait.</summary>
    public const string OverlaySuffix = "-overlay";

    /// <summary>Display order, most populous groupings first.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Female, Male, Neutral, Named, Background, Other };

    /// <summary>
    /// True for the overlay layer of another portrait. The name is exact: everything
    /// observed pairs "<name>-overlay.png" with a "<name>.png" that also exists.
    /// </summary>
    public static bool IsOverlay(string nameWithoutExtension) =>
        nameWithoutExtension.EndsWith(OverlaySuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of the portrait an overlay belongs to, or null when it is not one.</summary>
    public static string? BaseNameOf(string nameWithoutExtension) =>
        IsOverlay(nameWithoutExtension)
            ? nameWithoutExtension[..^OverlaySuffix.Length]
            : null;

    /// <summary>
    /// Groups a portrait by its file name. An overlay is grouped with the portrait it
    /// belongs to, so it never lands somewhere its base did not.
    /// </summary>
    public static string ForName(string? nameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(nameWithoutExtension)) return Other;

        var name = BaseNameOf(nameWithoutExtension) ?? nameWithoutExtension;

        // Backgrounds first: "BGF_1" would otherwise never be reached by the rest.
        if (IsBackground(name)) return Background;

        var gender = GenderSegment(name);
        if (gender is not null) return gender;

        // A named character: "UN" is the game's own prefix for its unique NPCs.
        if (name.StartsWith("UN_", StringComparison.OrdinalIgnoreCase)) return Named;

        return Other;
    }

    private static bool IsBackground(string name)
    {
        if (!name.StartsWith("BG", StringComparison.OrdinalIgnoreCase)) return false;

        // "BG_1" and "BGF_3" are backgrounds; a character called "BGirl" is not.
        var rest = name.AsSpan(2);
        if (rest.Length == 0) return true;

        var c = rest[0];
        return c is '_' or '-' || char.IsDigit(c)
               || (rest.Length > 1 && (c is 'F' or 'f') && (rest[1] is '_' or '-' || char.IsDigit(rest[1])));
    }

    /// <summary>
    /// Finds a whole "_f", "_m" or "_n" segment. Bounded on both sides so that the "n"
    /// inside a word, or an upper-case letter in a mod's own naming, is never mistaken
    /// for a gender marker.
    /// </summary>
    private static string? GenderSegment(string name)
    {
        for (var i = 0; i + 2 < name.Length + 1; i++)
        {
            if (name[i] != '_') continue;
            if (i + 1 >= name.Length) break;

            var letter = name[i + 1];
            if (letter is not ('f' or 'm' or 'n')) continue;

            // Must be followed by a separator, or be the very end of the name.
            var end = i + 2;
            if (end < name.Length && name[end] is not ('-' or '_')) continue;

            return letter switch
            {
                'f' => Female,
                'm' => Male,
                _ => Neutral,
            };
        }

        return null;
    }
}
