using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Overrides;

/// <summary>
/// How confident we are about which texture Elin will actually use.
/// The application states this openly rather than presenting a guess as fact.
/// </summary>
public enum WinnerConfidence
{
    /// <summary>Only one candidate, or every candidate is byte-identical.</summary>
    Certain,
    /// <summary>Several candidates, resolved by the load-order convention.</summary>
    Likely,
    /// <summary>Candidates whose relative priority cannot be determined from local data.</summary>
    Unknown,
}

/// <summary>
/// Which end of the load order wins a file conflict. LaterWins is what the game does:
/// packages are activated in load order, each appending its files to ModManager.replaceFiles,
/// and TextureManager.Init then applies that list with TextureData.AddReplace, which
/// assigns dictReplace[index] - so the last package to load overwrites the rest.
/// Kept as a setting so a user who observes otherwise can flip it without a code change.
/// </summary>
public enum PriorityConvention
{
    LaterWins,
    EarlierWins,
}

public sealed record TextureWinner(
    TextureFile? File,
    string Description,
    WinnerConfidence Confidence,
    bool IsManagerOverride)
{
    public static TextureWinner None { get; } =
        new(null, "No enabled mod supplies this texture", WinnerConfidence.Certain, false);
}

/// <summary>
/// Works out which version of a texture the game would load, from the mods that are
/// enabled, their position in loadorder.txt, and this application's override package.
/// </summary>
public sealed class WinnerResolver
{
    private readonly PriorityConvention _convention;

    public WinnerResolver(PriorityConvention convention = PriorityConvention.LaterWins)
        => _convention = convention;

    /// <summary>
    /// Resolves the winner for one texture ID.
    /// </summary>
    /// <param name="entry">The indexed texture.</param>
    /// <param name="modsByKey">Scanned mods, keyed as <see cref="ModPackage.Key"/>.</param>
    public TextureWinner Resolve(TextureEntry entry, IReadOnlyDictionary<string, ModPackage> modsByKey)
    {
        // Variants live in sub-folders and are never loaded by the game.
        var candidates = entry.Versions
            .Select(v => (file: v, mod: modsByKey.GetValueOrDefault(v.ModKey)))
            .Where(x => x.mod is null || x.mod.Enabled)
            .ToList();

        if (candidates.Count == 0) return TextureWinner.None;

        if (candidates.Count == 1)
        {
            var only = candidates[0].file;
            return only.SourceType == TextureSourceType.Override
                ? new TextureWinner(only, "Overridden by Elin Texture Manager",
                    WinnerConfidence.Likely, IsManagerOverride: true)
                : new TextureWinner(only, only.ModName, WinnerConfidence.Certain, false);
        }

        var ordered = OrderByPriority(candidates);

        // The override package is the whole point of this application, but it is still
        // just a package: anything the game loads after it replaces the same file again.
        // That happens as soon as a mod is added below it in loadorder.txt, so it is
        // checked rather than assumed.
        var overridden = candidates.FirstOrDefault(c => c.file.SourceType == TextureSourceType.Override);
        if (overridden.file is not null)
        {
            var beatenBy = ordered
                .TakeWhile(c => c.file.SourceType != TextureSourceType.Override)
                .FirstOrDefault(c => !SameImage(c.file, overridden.file));

            if (beatenBy.file is null)
            {
                return Tied(ordered, overridden, out var rival)
                    ? new TextureWinner(overridden.file,
                        $"Overridden by Elin Texture Manager (uncertain - ties with {rival})",
                        WinnerConfidence.Unknown, IsManagerOverride: true)
                    : new TextureWinner(overridden.file, "Overridden by Elin Texture Manager",
                        WinnerConfidence.Likely, IsManagerOverride: true);
            }

            return new TextureWinner(beatenBy.file,
                $"{beatenBy.file.ModName} - loads after your override, so the game shows this instead",
                Confidence(beatenBy), false);
        }

        var winner = ordered[0];

        // Byte-identical candidates make the question moot.
        if (candidates.All(c => SameImage(c.file, winner.file)))
        {
            return new TextureWinner(
                winner.file,
                $"{winner.file.ModName} (all {candidates.Count} versions are identical)",
                WinnerConfidence.Certain,
                false);
        }

        if (Tied(ordered, winner, out var tiedWith))
        {
            return new TextureWinner(winner.file,
                $"{winner.file.ModName} (uncertain - ties with {tiedWith}, and the game picks either)",
                WinnerConfidence.Unknown, false);
        }

        var confidence = Confidence(winner);
        var description = confidence == WinnerConfidence.Unknown
            ? $"{winner.file.ModName} (uncertain - its mod could not be found)"
            : winner.file.ModName;

        return new TextureWinner(winner.file, description, confidence, false);
    }

    private static bool SameImage(TextureFile a, TextureFile b) =>
        string.Equals(a.Hash ?? a.FullPath, b.Hash ?? b.FullPath, StringComparison.OrdinalIgnoreCase);

    private static WinnerConfidence Confidence((TextureFile file, ModPackage? mod) candidate) =>
        candidate.mod is null ? WinnerConfidence.Unknown : WinnerConfidence.Likely;

    /// <summary>
    /// Whether another candidate with a different image sits on exactly the same priority.
    /// The game sorts with List.Sort, which is not stable, so a tie has no defined winner.
    /// Only reachable for a mod missing from loadorder.txt whose package.xml priority
    /// happens to equal a listed mod's line number - listed mods each have their own line.
    /// </summary>
    private static bool Tied(List<(TextureFile file, ModPackage? mod)> ordered,
        (TextureFile file, ModPackage? mod) top, out string rival)
    {
        rival = "";
        if (top.mod is null) return false;

        var other = ordered.FirstOrDefault(c => c.mod is not null && c.mod != top.mod
            && c.mod.GamePriority == top.mod.GamePriority && !SameImage(c.file, top.file));

        if (other.file is null) return false;

        rival = other.file.ModName;
        return true;
    }

    /// <summary>Highest-priority candidate first.</summary>
    private List<(TextureFile file, ModPackage? mod)> OrderByPriority(
        List<(TextureFile file, ModPackage? mod)> candidates)
    {
        var ranked = candidates.ToList();

        ranked.Sort((a, b) =>
        {
            // A file whose mod was not scanned cannot be placed; rank it last rather than
            // let it claim a win nothing supports.
            if ((a.mod is null) != (b.mod is null)) return a.mod is null ? 1 : -1;

            var ai = a.mod?.GamePriority ?? int.MinValue;
            var bi = b.mod?.GamePriority ?? int.MinValue;

            var cmp = _convention == PriorityConvention.LaterWins
                ? bi.CompareTo(ai)
                : ai.CompareTo(bi);

            if (cmp != 0) return cmp;

            return string.Compare(a.file.ModName, b.file.ModName,
                StringComparison.CurrentCultureIgnoreCase);
        });

        return ranked;
    }

    /// <summary>Resolves winners for every indexed texture in one pass.</summary>
    public Dictionary<string, TextureWinner> ResolveAll(ScanResult scan)
    {
        var modsByKey = scan.Mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        var winners = new Dictionary<string, TextureWinner>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in scan.Index.Values)
            winners[entry.TextureId] = Resolve(entry, modsByKey);

        return winners;
    }
}
