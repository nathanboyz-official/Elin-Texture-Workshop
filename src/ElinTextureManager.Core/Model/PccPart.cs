namespace ElinTextureManager.Core.Model;

/// <summary>
/// The kind of body part a PCC file supplies.
///
/// Elin builds a character by layering these, and every file names its layer:
/// pcc_hair_x.png, pcc_cloth_y.png, pcc_body_z.png. Grouping by layer is what turns
/// three thousand loose files into something you can actually look through - "which
/// mod added this hairstyle" is a question about the hair layer, not about a filename.
///
/// The parts are also usually greyscale, tinted per character at runtime, so colour
/// tells you nothing about which file you are looking at. Layer is the only useful axis.
/// </summary>
public static class PccPart
{
    public const string Hair = "Hair";
    public const string Cloth = "Cloth";
    public const string Body = "Body";
    public const string Head = "Head";
    public const string Face = "Face";
    public const string Eye = "Eye";
    public const string Ride = "Ride";
    public const string Other = "Other";

    /// <summary>Display order, roughly outermost layer first.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Hair, Cloth, Body, Head, Face, Eye, Ride, Other };

    /// <summary>
    /// Taken from the names actually present across the installed mods and the base
    /// game, not from guesswork: the first pass invented a short list and left a third
    /// of every PCC file in "Other", which is no grouping at all.
    ///
    /// Longer tokens are matched first so "pcc_hairbk" (the back-of-head layer) does not
    /// get swallowed by "pcc_hair".
    /// </summary>
    private static readonly (string Token, string Part)[] Tokens =
    {
        ("pcc_subhair", Hair),
        ("pcc_hairbk", Hair),
        ("pcc_hair", Hair),
        ("pcc_mantlebk", Cloth),
        ("pcc_mantle", Cloth),
        ("pcc_cloth", Cloth),
        ("pcc_undie", Cloth),
        ("pcc_pants", Cloth),
        ("pcc_boots", Cloth),
        ("pcc_belt", Cloth),
        ("pcc_glove", Cloth),
        ("pcc_chest", Body),
        ("pcc_body", Body),
        ("pcc_head", Head),
        ("pcc_beard", Head),
        ("pcc_face", Face),
        ("pcc_eye", Eye),
        ("pcc_ride", Ride),
        ("pcc_etc", Other),
    };

    /// <summary>
    /// The layer a PCC file belongs to, from its name. Anything unrecognised is kept
    /// under <see cref="Other"/> rather than dropped - an unusual name must never make
    /// a mod's contents invisible.
    /// </summary>
    public static string ForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return Other;

        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        foreach (var (token, part) in Tokens.OrderByDescending(t => t.Token.Length))
            if (name.StartsWith(token, StringComparison.Ordinal)) return part;

        return Other;
    }
}
