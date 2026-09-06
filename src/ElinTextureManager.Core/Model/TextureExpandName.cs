namespace ElinTextureManager.Core.Model;

/// <summary>A TextureExpand file broken into the sprite it varies and the conditions.</summary>
public sealed record TextureExpandParts(string Sprite, IReadOnlyList<string> Conditions)
{
    /// <summary>True when this is the plain sprite rather than one of its conditions.</summary>
    public bool IsBase => Conditions.Count == 0;

    /// <summary>"drunk and asleep", for saying which state this covers.</summary>
    public string ConditionText => Conditions.Count == 0
        ? "normally"
        : string.Join(" + ", Conditions);
}

/// <summary>
/// Reads a TextureExpand file name.
///
/// TextureExpand lets a mod supply a different image for a sprite depending on the state
/// the thing is in, by hanging conditions off the file name with hashes:
///
///     objC_1303.png                          the ordinary picture
///     objC_1303#con-drunk.png                when it is drunk
///     objC_1303#fur#con-sleep#con-drunk.png  furred, asleep and drunk at once
///
/// Conditions stack, so one sprite can have eight files. That matters for more than
/// naming: each of those is a separate image that separate mods can replace, so one
/// character being fought over by two mods appears as eight arguments rather than one.
/// Seen through the file names it is eight problems; seen through the sprite it is one
/// decision, and the eight answers had better agree.
///
/// The conditions in use across a real library are con-drunk, con-sleep,
/// hostility-enemy, fur and month-winter.
/// </summary>
public static class TextureExpandName
{
    /// <summary>
    /// Splits a file name. Anything without a hash is the base, which is the answer for
    /// every ordinary texture too.
    /// </summary>
    public static TextureExpandParts Parse(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;

        // The path is not part of the sprite's identity - two mods put the same sprite in
        // folders of their own naming.
        var slash = name.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0) name = name[(slash + 1)..];

        var parts = name.Split('#', StringSplitOptions.RemoveEmptyEntries);

        // A name that is nothing but hashes leaves no sprite to speak of. Returning the
        // hashes themselves would invent a sprite id that groups unrelated files together.
        if (parts.Length == 0) return new TextureExpandParts(string.Empty, Array.Empty<string>());

        if (parts.Length == 1)
            return new TextureExpandParts(parts[0], Array.Empty<string>());

        return new TextureExpandParts(
            parts[0],
            parts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0).ToList());
    }

    /// <summary>The sprite a file belongs to, whether or not it carries conditions.</summary>
    public static string SpriteOf(string fileName) => Parse(fileName).Sprite;
}
