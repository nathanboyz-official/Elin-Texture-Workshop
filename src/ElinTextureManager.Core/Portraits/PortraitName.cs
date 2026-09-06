namespace ElinTextureManager.Core.Portraits;

/// <summary>Whether a proposed portrait name is one the game can load.</summary>
public sealed record PortraitNameCheck(bool Ok, string? Problem)
{
    public static readonly PortraitNameCheck Fine = new(true, null);
    public static PortraitNameCheck No(string problem) => new(false, problem);
}

/// <summary>
/// The rules a portrait file name in Elin\Custom\Portrait has to follow.
///
/// Looser than the PCC rules: nothing is parsed out of the name, so underscores are
/// welcome and the game's own files use them freely (special_n_custom1.png). Only two
/// things actually matter - it has to be a legal file name, and the "-overlay" ending
/// is reserved, because Elin treats a file called "x-overlay.png" as a layer drawn on
/// top of the portrait "x.png" rather than as a portrait of its own.
/// </summary>
public static class PortraitName
{
    /// <summary>The ending Elin reads as "this is a layer over another portrait".</summary>
    public const string OverlaySuffix = "-overlay";

    /// <summary>The ending Elin reads as "this is the full-size art for that portrait".</summary>
    public const string FullSuffix = "-full";

    /// <summary>The endings the game looks up as extra layers rather than as portraits.</summary>
    public static readonly string[] Reserved = { OverlaySuffix, FullSuffix };

    public const int MaxLength = 64;

    public static string FileName(string name) => name + ".png";

    public static PortraitNameCheck Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return PortraitNameCheck.No("Give it a name.");

        var trimmed = name.Trim();

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return PortraitNameCheck.No("That name cannot be part of a file name.");

        if (trimmed.Contains('.'))
            return PortraitNameCheck.No("No dots in the name - the extension is added for you.");

        if (trimmed.Length > MaxLength)
            return PortraitNameCheck.No("That name is too long.");

        foreach (var reserved in Reserved)
        {
            if (trimmed.EndsWith(reserved, StringComparison.OrdinalIgnoreCase))
                return PortraitNameCheck.No(
                    $"A name ending in \"{reserved}\" is read by the game as an extra layer "
                    + "of the portrait with the same name, not as a portrait of its own.");
        }

        return PortraitNameCheck.Fine;
    }

    /// <summary>
    /// A portrait name taken from the file the user picked: its own name, with anything
    /// a file name cannot hold removed. Someone choosing "My Cat (2).png" means to call
    /// it after their cat, and should not have to retype it.
    /// </summary>
    public static string FromFile(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path) ?? string.Empty;

        var cleaned = new string(stem
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '.')
            .ToArray())
            .Trim();

        if (cleaned.Length > MaxLength) cleaned = cleaned[..MaxLength];

        // A reserved ending would change what the file means, so it is not kept.
        var trimming = true;

        while (trimming)
        {
            trimming = false;

            foreach (var reserved in Reserved)
            {
                if (!cleaned.EndsWith(reserved, StringComparison.OrdinalIgnoreCase)) continue;

                cleaned = cleaned[..^reserved.Length].Trim();
                trimming = true;
            }
        }

        return cleaned.Length == 0 ? "portrait" : cleaned;
    }

    /// <summary>
    /// A name that is not already taken, by adding a number. Adding a portrait must
    /// never quietly overwrite one that is already there - that would be a replacement,
    /// which is the opposite of what this folder is for.
    /// </summary>
    public static string Available(string name, Func<string, bool> taken)
    {
        var baseName = FromFile(name);
        if (!taken(baseName)) return baseName;

        for (var n = 2; n < 500; n++)
        {
            var next = baseName + n;
            if (!taken(next)) return next;
        }

        return baseName + Guid.NewGuid().ToString("N")[..6];
    }
}
