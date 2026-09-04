namespace ElinTextureManager.Core.Pcc;

/// <summary>One row of the character editor: a layer the user picks a part for.</summary>
public sealed record PccSlot(string Layer, string Label, string? BackLayer = null)
{
    /// <summary>True for slots that bring a second, rear-facing piece with them.</summary>
    public bool HasBack => BackLayer is not null;
}

/// <summary>
/// The editor's slots, in the game's own order and using the game's own labels.
///
/// Both come from Elin rather than from invention. The labels are the strings the game
/// ships against its own keys - pcc_mantle is "Back", pcc_cloth is "Tops", pcc_belt is
/// "Waist" - so a hairstyle called Sub Hair here is called Sub Hair there.
///
/// Seventeen layers exist but only fifteen are rows, because two of them are never
/// chosen directly: hairbk and mantlebk are the backs of hair and mantle, and the game
/// finds them by the same unique id as the front. That is why the editor has one Hair
/// slider and not two, and why the front of one hairstyle cannot be worn with the back
/// of another without renaming files.
/// </summary>
public static class PccSlots
{
    public static IReadOnlyList<PccSlot> All { get; } = new[]
    {
        new PccSlot("body", "Body"),
        new PccSlot("undie", "Undie"),
        new PccSlot("etc", "Special"),
        new PccSlot("hair", "Hair", BackLayer: "hairbk"),
        new PccSlot("subhair", "Sub Hair"),
        new PccSlot("head", "Head"),
        new PccSlot("face", "Face"),
        new PccSlot("eye", "Eyes"),
        new PccSlot("mantle", "Back", BackLayer: "mantlebk"),
        new PccSlot("chest", "Chest"),
        new PccSlot("belt", "Waist"),
        new PccSlot("pants", "Bottoms"),
        new PccSlot("glove", "Hand"),
        new PccSlot("cloth", "Tops"),
        new PccSlot("boots", "Foot"),
    };

    /// <summary>The slot a layer belongs to, ignoring the two that are never picked directly.</summary>
    public static PccSlot? For(string? layer) =>
        layer is null ? null : All.FirstOrDefault(s =>
            string.Equals(s.Layer, layer, StringComparison.OrdinalIgnoreCase));

    /// <summary>The layer that carries the back of this one, if any.</summary>
    public static string? BackLayerOf(string? layer) => For(layer)?.BackLayer;

    /// <summary>Layers that are only ever reached through their front piece.</summary>
    public static bool IsBackLayer(string? layer) =>
        All.Any(s => string.Equals(s.BackLayer, layer, StringComparison.OrdinalIgnoreCase));

    /// <summary>A character with nothing on is still a character: it needs a body.</summary>
    public const string DefaultSet = "female";
    public const string BodyLayer = "body";
}
