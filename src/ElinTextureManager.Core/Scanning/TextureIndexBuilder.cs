using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Scanning;

/// <summary>
/// Turns the flat list of scanned files into the texture-ID index:
/// texture ID -> every installed version of it.
/// </summary>
public static class TextureIndexBuilder
{
    public static void Build(ScanResult result)
    {
        result.Index.Clear();

        foreach (var mod in result.Mods)
        {
            foreach (var texture in mod.Textures)
            {
                if (string.IsNullOrEmpty(texture.TextureId)) continue;

                if (!result.Index.TryGetValue(texture.TextureId, out var entry))
                {
                    entry = new TextureEntry
                    {
                        TextureId = texture.TextureId,
                        Prefix = texture.Prefix,
                        NumericId = texture.Identity.NumericId,
                        Kind = texture.Kind,
                    };
                    result.Index[texture.TextureId] = entry;
                }

                if (texture.IsVariant) entry.Variants.Add(texture);
                else entry.Versions.Add(texture);
            }
        }

        LinkPortraitOverlays(result);

        // A texture that only ever appears in variant sub-folders still deserves an entry,
        // but it must not look like an active conflict.
        foreach (var entry in result.Index.Values)
        {
            entry.Versions.Sort(static (a, b) =>
                string.Compare(a.ModName, b.ModName, StringComparison.CurrentCultureIgnoreCase));
            entry.Variants.Sort(static (a, b) =>
                string.Compare(a.ModName, b.ModName, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    /// <summary>
    /// Hangs each "-overlay" portrait off the portrait it belongs to, so the grid can show
    /// one tile per character instead of two. Every overlay observed - all 403 of them
    /// across the shipped portraits and every installed mod - has a base of the same name;
    /// one that does not is left as an ordinary entry rather than being hidden, because a
    /// picture with nothing to attach it to still has to be reachable.
    /// </summary>
    private static void LinkPortraitOverlays(ScanResult result)
    {
        foreach (var entry in result.Index.Values)
        {
            if (!entry.IsOverlay) continue;

            var baseName = PortraitGroup.BaseNameOf(entry.DisplayId);
            if (baseName is null) continue;

            if (!result.Index.TryGetValue(
                    TextureIdentity.PortraitNamespace + baseName, out var owner)) continue;

            owner.Overlay = entry;
            entry.OverlayOwnerId = owner.TextureId;
        }
    }

    /// <summary>Every distinct prefix seen, with how many texture IDs use it.</summary>
    public static IReadOnlyList<(string Prefix, int Count)> PrefixHistogram(ScanResult result) =>
        result.Index.Values
            .GroupBy(e => e.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(t => t.Item2)
            .ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
