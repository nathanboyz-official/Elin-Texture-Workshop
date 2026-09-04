namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// One installed PCC file, addressed the way a style addresses it.
///
/// Named for the file rather than the part because Model.PccPart already means the
/// layer a file belongs to, and the two would otherwise read as the same thing.
/// </summary>
public sealed record PccFile(string Layer, string Set, string Id, string FullPath, string ModName)
{
    /// <summary>True when this part also has a rear-facing piece to bring with it.</summary>
    public bool HasBack { get; init; }

    /// <summary>From the base game rather than a mod, which is what a default should be.</summary>
    public bool IsVanilla { get; init; }

    /// <summary>Made in this application, and therefore ours to delete.</summary>
    public bool IsMine { get; init; }
}

/// <summary>
/// Every PCC part installed, indexed the way a style file names one: by layer, set and
/// unique id.
///
/// The point of the index is resolution. A style says hair "3" from "female" and says
/// nothing about which mod supplies it or where it sits on disk - which is exactly what
/// makes styles small and shareable, and exactly what has to be looked up before
/// anything can be drawn.
/// </summary>
public sealed class PccLibrary
{
    private readonly Dictionary<string, PccFile> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PccFile>> _byLayer = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byKey.Count;

    public IReadOnlyList<PccFile> InLayer(string layer) =>
        _byLayer.TryGetValue(layer, out var list) ? list : Array.Empty<PccFile>();

    /// <summary>Adds a file. The set is the folder under Actor/PCC that holds it.</summary>
    public void Add(string layer, string set, string id, string fullPath, string modName,
        bool isVanilla = false, bool isMine = false)
    {
        var part = new PccFile(layer, set, id, fullPath, modName)
        {
            IsVanilla = isVanilla,
            IsMine = isMine,
        };

        // First one wins, matching the load order the caller feeds them in.
        if (!_byKey.TryAdd(Key(layer, set, id), part)) return;

        if (!_byLayer.TryGetValue(layer, out var list)) _byLayer[layer] = list = new();
        list.Add(part);
    }

    /// <summary>
    /// Finds the file a choice names.
    ///
    /// Falls back to the same id in any set, because a style written against one set
    /// still names a real part when a mod installed it somewhere else - and showing the
    /// part the user meant beats showing a gap.
    /// </summary>
    public PccFile? Resolve(string layer, PccChoice choice)
    {
        if (_byKey.TryGetValue(Key(layer, choice.Set, choice.FileId), out var exact)) return exact;

        return InLayer(layer).FirstOrDefault(p =>
            string.Equals(p.Id, choice.FileId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a front part has a back piece installed under the same id.</summary>
    public bool HasBackFor(string layer, PccChoice choice) =>
        PccSlots.BackLayerOf(layer) is { } back && Resolve(back, choice) is not null;

    /// <summary>
    /// The parts a style actually draws, in draw order, with their colours attached.
    /// Anything the style names that is not installed is skipped and reported.
    /// </summary>
    public (List<(string Layer, PccFile Part, PccChoice Choice)> Found, List<string> Missing)
        ResolveStyle(PccStyle style)
    {
        var found = new List<(string, PccFile, PccChoice)>();
        var missing = new List<string>();

        foreach (var (layer, choice) in style.Drawable())
        {
            var part = Resolve(layer, choice);

            if (part is not null) found.Add((layer, part, choice));
            else if (!PccSlots.IsBackLayer(layer)) missing.Add($"{layer} {choice.Id}");
        }

        found.Sort((a, b) => PccLayer.OrderOf(a.Item1).CompareTo(PccLayer.OrderOf(b.Item1)));
        return (found, missing);
    }

    /// <summary>
    /// A body, so a new character is never an empty canvas.
    ///
    /// The base game's own first body, in preference to a mod's. A default should be the
    /// thing everyone has, not whichever mod happened to be indexed first.
    /// </summary>
    public PccFile? DefaultBody() =>
        InLayer(PccSlots.BodyLayer)
            .Where(p => string.Equals(p.Set, PccSlots.DefaultSet, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.IsVanilla)
            .ThenBy(p => p.Id.Length)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>How a part is addressed, and how a favourite is remembered.</summary>
    public static string KeyOf(string layer, string set, string id) => $"{layer}|{set}|{id}";

    private static string Key(string layer, string set, string id) => KeyOf(layer, set, id);

    /// <summary>Forgets a part, for when its file has been deleted.</summary>
    public void Remove(PccFile part)
    {
        _byKey.Remove(Key(part.Layer, part.Set, part.Id));

        if (_byLayer.TryGetValue(part.Layer, out var list))
            list.RemoveAll(p => string.Equals(p.FullPath, part.FullPath, StringComparison.OrdinalIgnoreCase));
    }
}
