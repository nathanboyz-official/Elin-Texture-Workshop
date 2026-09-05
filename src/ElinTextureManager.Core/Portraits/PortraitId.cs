namespace ElinTextureManager.Core.Portraits;

/// <summary>One of the groups Elin offers a portrait under.</summary>
/// <remarks>
/// ToString is the label on purpose. A dropdown falls back to it whenever the list is of
/// something other than strings, and a record's generated ToString prints the type name
/// and every field - which is what the group box showed before this.
/// </remarks>
public sealed record PortraitGroupOption(string Code, string Label, string Note)
{
    public override string ToString() => Label;
}

/// <summary>Male, female, or offered to anyone.</summary>
public sealed record PortraitGenderOption(string Code, string Label, string Note)
{
    public override string ToString() => Label;
}

/// <summary>What a portrait file name breaks down into.</summary>
public sealed record PortraitIdParts(string Group, string Gender, string Name)
{
    /// <summary>Whether the game will ever list this portrait.</summary>
    public bool Listed => PortraitId.IsGroup(Group);
}

/// <summary>
/// The name a portrait file has to carry for Elin to offer it.
///
/// This is not a convention anyone chose - it is read straight out of the game. Portrait
/// listing does:
///
///     string[] array = item.id.Split('-')[0].Split('_');
///     if (array[0] == cat) { gender = array[1] == "m" ? 2 : array[1] == "f" ? 1 : 0; ... }
///
/// where the id is the file name without its extension. So everything before the first
/// hyphen is split on underscores: the first piece is the group and must match one the
/// game asks for, and the second decides who is offered it. A file whose first piece is
/// not one of those groups is never listed by anything - it sits in the folder, loads
/// fine, and is simply never shown. That is why a portrait called "afta.png" does
/// nothing: its group reads as "afta", which nothing ever asks for.
///
/// The groups are the ones the character screen asks for, in the order it asks:
///
///     ListPortraits(gender, "c").Concat(ListPortraits(gender, "guard"))
///         .Concat(ListPortraits(gender, "special")).Concat(ListPortraits(gender, "foxfolk"))
///
/// The shape used here is group_gender_name, which is what the game's own example file
/// uses - Custom\Portrait\special_n_custom1.png. Anything after the first hyphen is
/// ignored by the split, so a name may hold hyphens and underscores freely; only the two
/// reserved endings matter, because the game looks up "<id>-overlay" and "<id>-full"
/// as extra layers of the portrait rather than as portraits.
/// </summary>
public static class PortraitId
{
    /// <summary>The group used when nothing else is chosen; the game defaults to it too.</summary>
    public const string DefaultGroup = "c";

    public const string Male = "m";
    public const string Female = "f";

    /// <summary>
    /// Anything that is not "m" or "f" reads as no gender, and the portrait is offered
    /// whoever is asking. "n" is the letter the game's own example uses.
    /// </summary>
    public const string AnyGender = "n";

    public static readonly IReadOnlyList<PortraitGroupOption> Groups = new[]
    {
        new PortraitGroupOption("c", "Character",
            "The ordinary one. Offered when making a character."),
        new PortraitGroupOption("guard", "Guard", "Offered for guards."),
        new PortraitGroupOption("special", "Special", "Offered alongside the character ones."),
        new PortraitGroupOption("foxfolk", "Foxfolk", "Offered for foxfolk."),
    };

    public static readonly IReadOnlyList<PortraitGenderOption> Genders = new[]
    {
        new PortraitGenderOption(Male, "Male", "Offered when the character is male."),
        new PortraitGenderOption(Female, "Female", "Offered when the character is female."),
        new PortraitGenderOption(AnyGender, "Special", "Offered whoever is asking."),
    };

    public static bool IsGroup(string? code) =>
        code is not null && Groups.Any(g => string.Equals(g.Code, code, StringComparison.Ordinal));

    /// <summary>The id for a portrait: what the file is called without ".png".</summary>
    public static string Build(string group, string gender, string name)
    {
        var g = string.IsNullOrWhiteSpace(group) ? DefaultGroup : group.Trim();
        var s = string.IsNullOrWhiteSpace(gender) ? AnyGender : gender.Trim();

        return $"{g}_{s}_{name.Trim()}";
    }

    public static string FileName(string group, string gender, string name) =>
        Build(group, gender, name) + ".png";

    /// <summary>
    /// Reads an id back the way the game reads it, so a file already in the folder can be
    /// told apart from one the game will never look at.
    /// </summary>
    public static PortraitIdParts Parse(string id)
    {
        var head = id.Split('-')[0];
        var parts = head.Split('_');

        var group = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : DefaultGroup;
        var gender = parts.Length > 1 ? parts[1] : string.Empty;

        // Whatever is left is the portrait's own name, including any hyphenated tail.
        var namePieces = parts.Skip(2);
        var tail = id.Length > head.Length ? id[head.Length..] : string.Empty;
        var name = string.Join("_", namePieces) + tail;

        return new PortraitIdParts(
            group,
            gender is Male or Female ? gender : AnyGender,
            name);
    }

    /// <summary>An id that is not already taken, by adding a number.</summary>
    public static string Available(string group, string gender, string name,
        Func<string, bool> taken)
    {
        var candidate = Build(group, gender, name);
        if (!taken(candidate)) return candidate;

        for (var n = 2; n < 500; n++)
        {
            var next = Build(group, gender, name + n);
            if (!taken(next)) return next;
        }

        return Build(group, gender, name + Guid.NewGuid().ToString("N")[..6]);
    }
}
