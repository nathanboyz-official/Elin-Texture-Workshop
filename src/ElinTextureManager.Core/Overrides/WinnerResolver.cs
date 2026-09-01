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
/// Which end of loadorder.txt wins a file conflict. Elin's loadorder.txt is written
/// top-to-bottom by the game's own mod manager; later entries overriding earlier ones
/// is the convention this defaults to, and it is exposed as a setting so a user who
/// observes otherwise can flip it without a code change.
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

        // The override package is the whole point of this application: when it supplies
        // a texture, that is the one intended to win.
        var overridden = candidates.FirstOrDefault(c => c.file.SourceType == TextureSourceType.Override);
        if (overridden.file is not null)
        {
            return new TextureWinner(
                overridden.file,
                "Overridden by Elin Texture Manager",
                WinnerConfidence.Likely,
                IsManagerOverride: true);
        }

        if (candidates.Count == 1)
        {
            var only = candidates[0].file;
            return new TextureWinner(only, only.ModName, WinnerConfidence.Certain, false);
        }

        // Byte-identical candidates make the question moot.
        var distinctHashes = candidates
            .Select(c => c.file.Hash ?? c.file.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var ordered = OrderByPriority(candidates);
        var winner = ordered[0];

        if (distinctHashes == 1)
        {
            return new TextureWinner(
                winner.file,
                $"{winner.file.ModName} (all {candidates.Count} versions are identical)",
                WinnerConfidence.Certain,
                false);
        }

        // If any candidate is missing from loadorder.txt we cannot rank it against the rest.
        var unranked = candidates.Any(c => c.mod is null || !c.mod.InLoadOrderFile);
        var confidence = unranked ? WinnerConfidence.Unknown : WinnerConfidence.Likely;

        var description = confidence == WinnerConfidence.Unknown
            ? $"{winner.file.ModName} (uncertain - not all sources appear in loadorder.txt)"
            : winner.file.ModName;

        return new TextureWinner(winner.file, description, confidence, false);
    }

    /// <summary>Highest-priority candidate first.</summary>
    private List<(TextureFile file, ModPackage? mod)> OrderByPriority(
        List<(TextureFile file, ModPackage? mod)> candidates)
    {
        var ranked = candidates.ToList();

        ranked.Sort((a, b) =>
        {
            // Local packages sit outside loadorder.txt; treat them as loading after
            // Workshop items, matching how Elin keeps them separate from the ordered list.
            var aLocal = a.mod is { InLoadOrderFile: false };
            var bLocal = b.mod is { InLoadOrderFile: false };
            if (aLocal != bLocal) return aLocal ? -1 : 1;

            var ai = a.mod?.LoadOrderIndex ?? int.MinValue;
            var bi = b.mod?.LoadOrderIndex ?? int.MinValue;

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
