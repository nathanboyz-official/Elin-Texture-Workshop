namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// The layers a PCC character is built from, in the order they are drawn.
///
/// Taken from Elin's own modding documentation rather than guessed: back hair first,
/// then the body and what it wears, then the face and head on top. A file names its
/// layer in its own name - pcc_cloth_something.png - so this is also how a loose folder
/// of three thousand parts becomes a character.
///
/// The two "bk" layers are the back views of hair and mantle, which is why hairbk sits
/// behind everything: it is the hair that hangs behind the head.
/// </summary>
public static class PccLayer
{
    /// <summary>Back to front. Index in this list is the draw order.</summary>
    public static IReadOnlyList<string> DrawOrder { get; } = new[]
    {
        "hairbk",
        "mantlebk",
        "mantle",
        "body",
        "undie",
        "boots",
        "pants",
        "cloth",
        "chest",
        "belt",
        "glove",
        "eye",
        "hair",
        "subhair",
        "face",
        "head",
        "etc",
    };

    /// <summary>
    /// The layer a file belongs to, from its name, or null when the name does not
    /// follow the convention.
    ///
    /// Longest match wins, so pcc_hairbk is the back hair rather than the hair: the
    /// short token is a prefix of the long one and testing in the wrong order would put
    /// every back-hair part in front of the face.
    /// </summary>
    public static string? Of(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        if (!name.StartsWith("pcc_", StringComparison.Ordinal)) return null;

        var rest = name[4..];

        return DrawOrder
            .Where(layer => rest.StartsWith(layer, StringComparison.Ordinal))
            .OrderByDescending(layer => layer.Length)
            .FirstOrDefault();
    }

    /// <summary>Where a layer sits in the stack; -1 for anything unrecognised.</summary>
    public static int OrderOf(string? layer) =>
        layer is null ? -1 : DrawOrder.ToList().IndexOf(layer);

    /// <summary>
    /// The part of the name that identifies the piece, after the layer.
    ///
    /// The documentation says this must not contain underscores, which makes it a thing
    /// worth reading back: a part named pcc_cloth_my_coat.png is not what its author
    /// thinks it is.
    /// </summary>
    public static string? UniqueIdOf(string? fileName)
    {
        var layer = Of(fileName);
        if (layer is null) return null;

        var name = Path.GetFileNameWithoutExtension(fileName!);
        var afterLayer = name[(4 + layer.Length)..];

        return afterLayer.TrimStart('_');
    }
}
