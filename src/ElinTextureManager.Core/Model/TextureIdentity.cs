using System.Globalization;

namespace ElinTextureManager.Core.Model;

/// <summary>
/// The identity parsed out of a texture replacement filename.
/// Elin matches replacement textures by file name, so the name is the key.
/// </summary>
public sealed record TextureIdentity(string TextureId, string Prefix, int? NumericId)
{
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

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (c is < '0' or > '9') return false;
        return true;
    }
}
