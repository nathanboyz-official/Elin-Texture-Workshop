using System.Globalization;

namespace ElinTextureManager.Core.Pcc;

/// <summary>One layer's choice: which set it comes from, which part, and its tint.</summary>
public sealed class PccChoice
{
    /// <summary>The folder under Actor/PCC. Everything the editor writes says "female".</summary>
    public string Set { get; set; } = PccSlots.DefaultSet;

    /// <summary>The part's unique id - the tail of pcc_layer_id.png.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The id with any file extension removed.
    ///
    /// Styles saved by older versions of the game store "24.png" where newer ones store
    /// "24". Both name the same part, so anything matching a choice to a file has to
    /// look past it.
    /// </summary>
    public string FileId => Id.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? Id[..^4]
        : Id;

    /// <summary>Six hex digits, or null for the part's own colours.</summary>
    public string? Colour { get; set; }

    public PccChoice Copy() => new() { Set = Set, Id = Id, Colour = Colour };

    /// <summary>The tint as RGB bytes, or null when the part is drawn untinted.</summary>
    public (byte R, byte G, byte B)? Rgb()
    {
        if (Colour is not { Length: 6 }) return null;

        return byte.TryParse(Colour[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
               && byte.TryParse(Colour[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
               && byte.TryParse(Colour[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? (r, g, b)
            : null;
    }
}

/// <summary>
/// A character, as Elin stores one: a layer name against a set, a part id and a colour.
///
/// This is the whole of an Elin appearance. There is no image in it - it names parts
/// that must already be installed, which is why a style is a few hundred bytes and why
/// sharing one is sharing a choice rather than someone else's art.
/// </summary>
public sealed class PccStyle
{
    /// <summary>Layer name to choice, in no particular order - the file is a map.</summary>
    public Dictionary<string, PccChoice> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where it was read from, when it came off disk.</summary>
    public string? SourcePath { get; set; }

    /// <summary>The name to show. A fav slot has no name of its own, so it gets one.</summary>
    public string Name { get; set; } = "New character";

    public PccChoice? Get(string layer) => Parts.GetValueOrDefault(layer);

    public void Set(string layer, PccChoice? choice)
    {
        if (choice is null || string.IsNullOrEmpty(choice.Id)) Parts.Remove(layer);
        else Parts[layer] = choice;
    }

    public PccStyle Copy()
    {
        var copy = new PccStyle { Name = Name, SourcePath = SourcePath };
        foreach (var (layer, choice) in Parts) copy.Parts[layer] = choice.Copy();
        return copy;
    }

    /// <summary>
    /// The layers to draw, back piece included.
    ///
    /// A back piece is not stored: it is found by giving its own layer the front's id,
    /// which is how the game does it and why the two cannot be mixed.
    /// </summary>
    public IEnumerable<(string Layer, PccChoice Choice)> Drawable()
    {
        foreach (var (layer, choice) in Parts)
        {
            if (string.IsNullOrEmpty(choice.Id)) continue;

            yield return (layer, choice);

            if (PccSlots.BackLayerOf(layer) is { } back)
                yield return (back, choice);
        }
    }
}
