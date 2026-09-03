using System.Globalization;

namespace ElinTextureManager.Core.Model;

/// <summary>
/// The identity parsed out of a texture replacement filename.
/// Elin matches replacement textures by file name, so the name is the key.
/// </summary>
public sealed record TextureIdentity(string TextureId, string Prefix, int? NumericId)
{
    /// <summary>
    /// Namespace applied to portrait IDs. Portraits and "Texture Replace" sprites are
    /// matched by file name inside *different* folders, so the same name in each is two
    /// different images. Namespacing keeps them apart in the index; the prefix is an
    /// implementation detail and is stripped again by <see cref="Display"/>.
    /// </summary>
    public const string PortraitNamespace = "portrait:";

    /// <summary>
    /// Parses a file name such as "objC_2115.png" into id/prefix/number.
    /// Never throws: unparseable names still yield a usable identity so that one odd
    /// file cannot take a scan down.
    /// </summary>
    public static TextureIdentity Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new TextureIdentity(string.Empty, string.Empty, null);

        var name = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (string.IsNullOrEmpty(name))
            return new TextureIdentity(string.Empty, string.Empty, null);

        // Split on the LAST underscore. "objC_2115" -> objC / 2115.
        // "objs_S_snow" has no trailing number, so the whole name stays the prefix.
        var cut = name.LastIndexOf('_');
        if (cut > 0 && cut < name.Length - 1)
        {
            var head = name[..cut];
            var tail = name[(cut + 1)..];
            if (IsAllDigits(tail) &&
                int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return new TextureIdentity(name, head, n);
            }
        }

        return new TextureIdentity(name, name, null);
    }

    /// <summary>Namespace prefix per replacement kind. See <see cref="PortraitNamespace"/>.</summary>
    public static string NamespaceFor(ReplacementKind kind) => kind switch
    {
        ReplacementKind.Portrait => PortraitNamespace,
        ReplacementKind.Pcc => "pcc:",
        ReplacementKind.Texture => "texture:",
        ReplacementKind.TextureExpand => "te:",
        _ => string.Empty,
    };

    /// <summary>
    /// Identity for a file in a "Portrait" folder. Portraits are addressed by their whole
    /// vanilla file name ("UN_ashland.png", "special_f-Alice-TCO.png"), which has no
    /// index to parse, so the name is kept intact under the portrait namespace.
    ///
    /// The prefix carries the portrait's group (Female, Male, Background â€¦) rather than a
    /// constant, so the grid's prefix filter says something useful about 2000 portraits.
    /// The category does not come from the prefix for portraits - see TextureFile.Category.
    /// </summary>
    public static TextureIdentity ForPortrait(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new TextureIdentity(string.Empty, string.Empty, null);

        var name = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (string.IsNullOrEmpty(name))
            return new TextureIdentity(string.Empty, string.Empty, null);

        return new TextureIdentity(PortraitNamespace + name, PortraitGroup.ForName(name), null);
    }

    /// <summary>
    /// Identity for a file in any replacement folder.
    ///
    /// <paramref name="relativePath"/> is the path below the replacement root, so a PCC
    /// part keeps its "female\" or "male\" folder: the same hair file name can exist for
    /// both, and they are different images. The separator is normalised so an ID does not
    /// depend on which way the slashes leaned when it was scanned.
    /// </summary>
    public static TextureIdentity ForReplacement(ReplacementKind kind, string relativePath)
    {
        if (kind == ReplacementKind.TextureReplace) return Parse(Path.GetFileName(relativePath));
        if (kind == ReplacementKind.Portrait) return ForPortrait(Path.GetFileName(relativePath));

        if (string.IsNullOrWhiteSpace(relativePath))
            return new TextureIdentity(string.Empty, string.Empty, null);

        var withoutExtension = Path.Combine(
            Path.GetDirectoryName(relativePath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(relativePath));

        var id = withoutExtension.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
        if (id.Length == 0) return new TextureIdentity(string.Empty, string.Empty, null);

        var fileName = Path.GetFileName(relativePath);

        var group = kind switch
        {
            ReplacementKind.Pcc => PccPart.ForFileName(fileName),
            // A TextureExpand variant is "<sprite>#<condition>"; the condition is the
            // useful axis, since the sprite it varies is already in the name.
            ReplacementKind.TextureExpand => ConditionOf(fileName),
            _ => Parse(fileName).Prefix,
        };

        return new TextureIdentity(NamespaceFor(kind) + id, group, null);
    }

    /// <summary>The "#condition" part of a TextureExpand file name, or "base" when plain.</summary>
    private static string ConditionOf(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var hash = name.IndexOf('#');
        return hash >= 0 && hash < name.Length - 1 ? name[(hash + 1)..] : "base";
    }

    /// <summary>Strips the index namespace from an ID so the user sees the plain name.</summary>
    public static string Display(string? textureId)
    {
        if (string.IsNullOrEmpty(textureId)) return string.Empty;

        foreach (var kind in ReplacementKindExtensions.All)
        {
            var ns = NamespaceFor(kind);
            if (ns.Length > 0 && textureId.StartsWith(ns, StringComparison.Ordinal))
                return textureId[ns.Length..];
        }

        return textureId;
    }

    /// <summary>True when the ID belongs to a portrait rather than a sprite.</summary>
    public static bool IsPortraitId(string? textureId) =>
        textureId is not null && textureId.StartsWith(PortraitNamespace, StringComparison.Ordinal);

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (c is < '0' or > '9') return false;
        return true;
    }
}
