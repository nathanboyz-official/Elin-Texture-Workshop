namespace ElinTextureManager.Core.Model;

/// <summary>
/// The Steam Workshop sections a mod belongs to, taken from the &lt;tags&gt; element of
/// package.xml - the same tags the author picks when publishing to the Workshop.
///
/// Tags are author-typed free text, so casing and spelling vary ("QoL", "Qol", "sprite",
/// "NPC Sprite"). They are normalised into a fixed set of sections; anything that does
/// not map is deliberately NOT discarded - it is kept as a free-form tag and the mod
/// lands in <see cref="Other"/>, so an unusual tag can never hide a mod.
/// </summary>
public static class ModSection
{
    public const string General = "General";
    public const string Sprite = "Sprite";
    public const string Portrait = "Portrait";
    public const string Npc = "NPC";
    public const string Pcc = "PCC";
    public const string Item = "Item";
    public const string Race = "Race";
    public const string Job = "Job";
    public const string Skill = "Skill";
    public const string Quest = "Quest";
    public const string Balance = "Balance";
    public const string QoL = "QoL";
    public const string Ui = "UI";
    public const string Utility = "Utility";
    public const string Library = "Library";
    public const string Cheat = "Cheat";
    public const string Bgm = "BGM";
    public const string Graphic = "Graphic";
    public const string Other = "Other";

    /// <summary>Sections in display order. Mirrors the Workshop's own ordering habits.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Sprite, Portrait, Npc, Pcc, Race, Item, Graphic,
        General, QoL, Balance, Job, Skill, Quest, Ui, Utility, Library, Cheat, Bgm,
        Other,
    };

    private static readonly Dictionary<string, string> Canonical =
        All.Where(s => s != Other).ToDictionary(s => s, s => s, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Spellings seen in the wild that mean an existing section. A tag mapping to several
    /// sections ("Item/NPC Sprite") contributes to all of them.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["npc sprite"] = new[] { Npc, Sprite },
            ["item/npc sprite"] = new[] { Item, Npc, Sprite },
            ["skill/ability"] = new[] { Skill },
            ["ability"] = new[] { Skill },
            ["class"] = new[] { Job },
            ["music"] = new[] { Bgm },
            ["sound"] = new[] { Bgm },
            ["interface"] = new[] { Ui },
            ["tweak"] = new[] { QoL },
            ["quality of life"] = new[] { QoL },
        };

    /// <summary>
    /// Turns one raw tag into zero or more sections. Compound tags are split on '/' so
    /// "Skill/Ability" is not lost, and a stray "タグ" ("tag") prefix is stripped.
    /// </summary>
    public static IEnumerable<string> Sections(string? rawTag)
    {
        var tag = Clean(rawTag);
        if (tag.Length == 0) yield break;

        if (Aliases.TryGetValue(tag, out var mapped))
        {
            foreach (var m in mapped) yield return m;
            yield break;
        }

        if (Canonical.TryGetValue(tag, out var exact))
        {
            yield return exact;
            yield break;
        }

        // Compound tags: take whichever halves we recognise, ignore the rest.
        var matchedAny = false;
        foreach (var part in tag.Split('/', StringSplitOptions.RemoveEmptyEntries
                                             | StringSplitOptions.TrimEntries))
        {
            if (Canonical.TryGetValue(part, out var hit)) { matchedAny = true; yield return hit; }
            else if (Aliases.TryGetValue(part, out var alias))
            {
                matchedAny = true;
                foreach (var m in alias) yield return m;
            }
        }

        if (!matchedAny) yield break;
    }

    /// <summary>Every section a mod's tags place it in. Never empty: falls back to Other.</summary>
    public static IReadOnlyList<string> SectionsFor(ModPackage mod)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in mod.Tags)
        foreach (var section in Sections(tag))
            set.Add(section);

        if (set.Count == 0) return new[] { Other };

        // Preserve the display order of All rather than alphabetical.
        return All.Where(set.Contains).ToList();
    }

    /// <summary>Tags that mapped to nothing, kept so they stay visible and searchable.</summary>
    public static IReadOnlyList<string> FreeFormTags(ModPackage mod) =>
        mod.Tags.Where(t => !Sections(t).Any())
            .Select(Clean)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var tag = raw.Trim();

        // One published mod tags itself "タグGeneral" - the Japanese word for "tag"
        // followed by the real tag. Strip it rather than inventing a section for it.
        const string tagWord = "\u30bf\u30b0";
        if (tag.StartsWith(tagWord, StringComparison.Ordinal)) tag = tag[tagWord.Length..].Trim();

        return tag;
    }
}
