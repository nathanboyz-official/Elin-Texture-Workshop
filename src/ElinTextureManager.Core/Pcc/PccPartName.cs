namespace ElinTextureManager.Core.Pcc;

/// <summary>Whether a proposed part name is one the game can actually load.</summary>
public sealed record PccNameCheck(bool Ok, string? Problem)
{
    public static readonly PccNameCheck Fine = new(true, null);
    public static PccNameCheck No(string problem) => new(false, problem);
}

/// <summary>
/// The rules a PCC file name has to follow.
///
/// Not house style: the game reads the layer and the id out of the file name, splitting
/// on underscores, so an id containing one is read as a different layer's part and the
/// file quietly never loads. Elin's own documentation says so, and it is the kind of
/// thing that costs an evening to work out from a part that simply never appears.
/// </summary>
public static class PccPartName
{
    /// <summary>What a saved part is called: pcc_<layer>_<id>.png.</summary>
    public static string FileName(string layer, string id) => $"pcc_{layer}_{id}.png";

    public static PccNameCheck Check(string? layer, string? id)
    {
        if (string.IsNullOrWhiteSpace(layer) || PccLayer.OrderOf(layer) < 0)
            return PccNameCheck.No("That is not a layer the game knows.");

        if (string.IsNullOrWhiteSpace(id))
            return PccNameCheck.No("Give it a name.");

        var trimmed = id.Trim();

        if (trimmed.Contains('_'))
            return PccNameCheck.No(
                "No underscores in the name - the game splits the file name on them, so "
                + "an id with one in it is read as a different layer and never loads.");

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return PccNameCheck.No("That name cannot be part of a file name.");

        if (trimmed.Contains('.'))
            return PccNameCheck.No("No dots in the name - the extension is added for you.");

        if (trimmed.Length > 48)
            return PccNameCheck.No("That name is too long.");

        return PccNameCheck.Fine;
    }

    /// <summary>
    /// A name that is not already taken, by adding a number.
    ///
    /// Copying a part called "cme50" gives "cme50copy", then "cme50copy2". A sprite
    /// started from nothing passes no suffix, because calling it a copy of something
    /// that does not exist is a small lie the file name then carries forever.
    /// </summary>
    public static string Available(string id, Func<string, bool> taken, string suffix = "copy")
    {
        var baseName = id.Replace("_", string.Empty).Replace(".", string.Empty);
        if (baseName.Length == 0) baseName = "mine";

        var candidate = baseName + suffix;
        if (!taken(candidate)) return candidate;

        for (var n = 2; n < 500; n++)
        {
            var next = candidate + n;
            if (!taken(next)) return next;
        }

        return candidate + Guid.NewGuid().ToString("N")[..6];
    }
}
