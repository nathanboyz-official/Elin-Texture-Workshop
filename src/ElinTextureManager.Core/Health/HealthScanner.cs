using ElinTextureManager.Core.Workshop;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.GameLog;
using ElinTextureManager.Core.Sheets;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.Core.Health;

/// <summary>
/// Answers "why is my game broken?" by inspecting what is installed rather than by
/// reading the crash dialog, which names the Harmony patches around a failing call and
/// so usually accuses an innocent mod.
///
/// Nothing here loads or executes mod code; every check reads metadata and files.
/// </summary>
public sealed class HealthScanner
{
    public const string ApiCheck = "Game API";
    public const string DuplicateCheck = "Duplicate code";
    public const string PatchCheck = "Patch conflict";
    public const string LoadOrderCheck = "Load order";
    public const string VersionCheck = "Version drift";
    public const string DependencyCheck = "Missing dependency";

    private IReadOnlyDictionary<string, WorkshopItem>? _workshop;

    public const string WorkshopCheck = "Workshop";

    /// <summary>
    /// Runs every check. <paramref name="workshop"/> is what Steam last said about the
    /// installed items, or null when the user has not turned that on - the checks that
    /// need it are simply skipped rather than guessing.
    /// </summary>
    public HealthReport Scan(ElinPaths paths, ScanResult scan, LoadOrderDocument loadOrder,
        IReadOnlyDictionary<string, WorkshopItem>? workshop = null,
        SelectionStore? selections = null)
    {
        var report = new HealthReport();
        _workshop = workshop;

        var codeMods = scan.Mods
            .Where(m => m.SourceType != TextureSourceType.Vanilla)
            .Select(m => (Mod: m, Dlls: FindAssemblies(m.Directory)))
            .Where(x => x.Dlls.Count > 0)
            .ToList();

        report.CodeModCount = codeMods.Count;

        CheckApi(paths, codeMods, report);
        CheckDuplicateAssemblies(codeMods, report);
        CheckPatchConflicts(codeMods, report);
        CheckLoadOrder(paths, scan, loadOrder, report);
        CheckVersions(paths, codeMods, report);
        CheckDependencies(paths, codeMods, report);
        CheckWorkshop(scan, report);
        CheckSourceSheets(scan, report);
        CheckPlayerLog(paths, report);
        CheckIdenticalConflicts(scan, report);
        CheckTextureExpandSprites(scan, selections, report);
        CheckLoadPriorities(scan, report);
        CheckShippedJunk(scan, report);

        report.Findings.Sort((a, b) => a.Severity != b.Severity
            ? a.Severity.CompareTo(b.Severity)
            : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));

        AppLog.Info($"Health scan: {report.Findings.Count} findings across "
                    + $"{report.CodeModCount} mods that ship code.");
        return report;
    }

    /// <summary>
    /// The spreadsheets mods add characters, items and the rest with.
    ///
    /// Worth checking here rather than nowhere: the game reads these silently, so a tab
    /// named wrong or a single blank row throws content away without anything being said,
    /// in the game or out of it. Most mods have no sheets at all and cost nothing.
    /// </summary>
    private static void CheckSourceSheets(ScanResult scan, HealthReport report)
    {
        foreach (var mod in scan.Mods.Where(m => m.SourceType != TextureSourceType.Vanilla))
        {
            foreach (var book in FindWorkbooks(mod.Directory))
            {
                foreach (var finding in SourceSheetChecker.Check(book, mod.Name, mod.Key))
                    report.Findings.Add(finding);
            }
        }
    }

    /// <summary>
    /// What the game itself wrote down last time it ran.
    ///
    /// Nobody reads Player.log - it is five thousand lines of Unity start-up with a
    /// handful of meaningful ones buried in it, and it is not even under the game's
    /// folder. Reading it here is the difference between guessing at what is wrong and
    /// being told.
    /// </summary>
    private static void CheckPlayerLog(ElinPaths paths, HealthReport report)
    {
        foreach (var finding in PlayerLogReader.Read(paths.PlayerLog))
            report.Findings.Add(finding);
    }

    public const string SameImageCheck = "Identical conflict";
    public const string ExpandCheck = "TextureExpand";
    public const string PriorityCheck = "Load priority";
    public const string JunkCheck = "Shipped by mistake";

    /// <summary>
    /// Mods asking for a load priority the game will not give them.
    ///
    /// The game clamps to -999..999 and silently ignores anything that is not a number.
    /// So a mod written with 114514 in it does not load after everything - it lands on
    /// 999 with every other mod that overreached, and which of them wins is then decided
    /// by something none of their authors chose. That matters most where a mod's whole
    /// job depends on being last, which is exactly what the TextureExpand mods do.
    /// </summary>
    private static void CheckLoadPriorities(ScanResult scan, HealthReport report)
    {
        var clamped = scan.Mods
            .Where(m => m.SourceType != TextureSourceType.Vanilla && m.LoadPriorityWasClamped)
            .ToList();

        var unreadable = scan.Mods
            .Where(m => m.SourceType != TextureSourceType.Vanilla && m.LoadPriorityUnreadable)
            .ToList();

        if (clamped.Count > 0)
        {
            // Who they now share the position with, since that is the actual consequence.
            var landing = clamped
                .Select(m => m.LoadPriority ?? PackageLimits.DefaultLoadPriority)
                .Distinct()
                .ToList();

            var neighbours = scan.Mods
                .Where(m => m.SourceType != TextureSourceType.Vanilla
                            && !m.LoadPriorityWasClamped
                            && m.LoadPriority is { } p && landing.Contains(p))
                .Select(m => m.Name)
                .Take(6)
                .ToList();

            var finding = new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = PriorityCheck,
                Title = clamped.Count == 1
                    ? $"{clamped[0].Name} asks for a load priority the game will not give it"
                    : $"{clamped.Count} mods ask for a load priority the game will not give them",
                Detail = "The game clamps load priority to -999..999. These asked for more, "
                         + "so instead of loading where their authors intended they land on "
                         + "the limit - together, and with anything already there. Which of "
                         + "them wins is then decided by nothing in particular.",
                Suggestion = "Nothing to fix in the mods themselves. Worth knowing when a "
                             + "mod that has to load last does not appear to.",
            };

            foreach (var mod in clamped.Take(10))
            {
                finding.ModNames.Add(mod.Name);
                finding.Evidence.Add($"{mod.Name} — asks for {mod.DeclaredLoadPriority}, "
                                     + $"gets {mod.LoadPriority}");
            }

            if (neighbours.Count > 0)
                finding.Evidence.Add("already there: " + string.Join(", ", neighbours));

            report.Findings.Add(finding);
        }

        if (unreadable.Count == 0) return;

        var bad = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = PriorityCheck,
            Title = unreadable.Count == 1
                ? $"{unreadable[0].Name} has a load priority the game cannot read"
                : $"{unreadable.Count} mods have a load priority the game cannot read",
            Detail = "The game reads this with int.TryParse and does nothing when that "
                     + $"fails, so the mod stays on the default of {PackageLimits.DefaultLoadPriority} "
                     + "with no complaint from anywhere.",
            Suggestion = "Only the mod's author can fix it. Worth knowing if the mod seems "
                         + "to load in the wrong place.",
        };

        foreach (var mod in unreadable.Take(10))
        {
            bad.ModNames.Add(mod.Name);
            bad.Evidence.Add($"{mod.Name} — package.xml says \"{mod.DeclaredLoadPriority}\"");
        }

        report.Findings.Add(bad);
    }

    /// <summary>Files that were never meant to be published, found inside installed mods.</summary>
    private static readonly (string What, Func<string, bool> Match)[] Junk =
    {
        ("drawing source", p => Path.GetExtension(p) is ".psd" or ".xcf" or ".clip"
                                    or ".aseprite" or ".ase" or ".sai2"),
        ("build output", p => Path.GetExtension(p) is ".pdb" or ".cs"),
        ("a debug log", p => Path.GetExtension(p) == ".log"
                             || p.Contains($"{Path.DirectorySeparatorChar}.Cache{Path.DirectorySeparatorChar}",
                                 StringComparison.OrdinalIgnoreCase)),
        ("system litter", p => Path.GetFileName(p) is "Thumbs.db" or "desktop.ini" or ".DS_Store"),
    };

    /// <summary>
    /// Things shipped to the Workshop that nobody meant to ship.
    ///
    /// Mostly harmless to a player, and worth knowing to an author: a drawing source file
    /// is the working copy of the art, and publishing it is usually an accident rather
    /// than a licence. One mod in a real library ships a megabyte and a half of its
    /// author's own build log, still full of paths from their E: drive.
    /// </summary>
    private static void CheckShippedJunk(ScanResult scan, HealthReport report)
    {
        var byMod = new List<(ModPackage Mod, string What, string File, long Bytes)>();

        foreach (var mod in scan.Mods.Where(m => m.SourceType != TextureSourceType.Vanilla))
        {
            IEnumerable<string> files;

            try { files = Directory.EnumerateFiles(mod.Directory, "*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var file in files)
            {
                var hit = Junk.FirstOrDefault(j => j.Match(file));
                if (hit.What is null) continue;

                long size;
                try { size = new FileInfo(file).Length; } catch { size = 0; }

                byMod.Add((mod, hit.What, Path.GetFileName(file), size));
            }
        }

        if (byMod.Count == 0) return;

        var mods = byMod.Select(j => j.Mod.Name).Distinct().Count();
        var bytes = byMod.Sum(j => j.Bytes);

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = JunkCheck,
            Title = $"{mods} mods ship files that were probably not meant to be published",
            Detail = $"{byMod.Count} files, {bytes / 1024 / 1024.0:0.#} MB - drawing sources, "
                     + "build output and debug logs. Harmless to play with, and worth "
                     + "knowing about if one of them is yours: a drawing source is the "
                     + "working copy of the art, and publishing it is usually an accident.",
            Suggestion = "Nothing to do unless it is your own mod.",
        };

        foreach (var group in byMod
                     .GroupBy(j => j.Mod.Name)
                     .OrderByDescending(g => g.Sum(j => j.Bytes))
                     .Take(10))
        {
            var biggest = group.OrderByDescending(j => j.Bytes).First();

            finding.Evidence.Add($"{group.Key} — {group.Count()} files, "
                                 + $"{group.Sum(j => j.Bytes) / 1024.0:N0} KB "
                                 + $"(e.g. {biggest.File}, {biggest.What})");
        }

        report.Findings.Add(finding);
    }

    /// <summary>
    /// Sprites whose TextureExpand conditions have been left behind.
    ///
    /// TextureExpand gives a sprite a different image when the thing is drunk, asleep,
    /// hostile and so on, as separate files with the condition in the name. Two mods
    /// replacing one character therefore argue eight times rather than once, and the
    /// conflict list shows eight rows - so it is entirely natural to settle the ordinary
    /// picture and never notice the other seven.
    ///
    /// The result is a character who looks like one mod's work until it falls asleep and
    /// then looks like another's. Nothing reports that, in the game or out of it, because
    /// each row was answered correctly on its own terms.
    /// </summary>
    private static void CheckTextureExpandSprites(ScanResult scan,
        SelectionStore? selections, HealthReport report)
    {
        if (selections is null) return;

        // Grouped across kinds on purpose. A sprite's ordinary picture lives in
        // "Texture Replace" and its conditioned ones in "TextureforTE", so they arrive
        // here as different kinds under different ids - which is exactly why nothing has
        // ever connected them, and why settling one and not the other is so easy to do.
        var bySprite = new Dictionary<string, List<TextureEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in scan.Conflicts)
        {
            var name = entry.Versions.FirstOrDefault()?.FileName ?? entry.DisplayId;
            var sprite = TextureExpandName.SpriteOf(name);
            if (sprite.Length == 0) continue;

            if (!bySprite.TryGetValue(sprite, out var list))
                bySprite[sprite] = list = new List<TextureEntry>();

            list.Add(entry);
        }

        var split = new List<string>();
        var rows = 0;

        foreach (var (sprite, entries) in bySprite)
        {
            // Only sprites that actually have conditions. Everything else is an ordinary
            // texture and a group of one.
            var conditioned = entries
                .Where(e => !TextureExpandName.Parse(
                    e.Versions.FirstOrDefault()?.FileName ?? e.DisplayId).IsBase)
                .ToList();

            if (conditioned.Count == 0) continue;

            var settled = entries.Where(e => selections.Has(e.TextureId)).ToList();
            var open = conditioned.Where(e => !selections.Has(e.TextureId)).ToList();

            // Only interesting where some of the sprite is decided and some is not. A
            // sprite nobody has touched is just an ordinary conflict waiting its turn.
            if (settled.Count == 0 || open.Count == 0) continue;

            var chosen = settled
                .Select(e => selections.Get(e.TextureId)?.SourceModName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var conditions = open
                .Select(e => TextureExpandName.Parse(
                    e.Versions.FirstOrDefault()?.FileName ?? e.DisplayId).ConditionText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4);

            rows += open.Count;

            split.Add($"{sprite} — you chose {string.Join(" / ", chosen)}, but "
                      + $"{open.Count} of its conditions are undecided ({string.Join(", ", conditions)})");
        }

        if (split.Count == 0) return;

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Conflict,
            Check = ExpandCheck,
            Title = $"{split.Count} sprites will change appearance when drunk or asleep",
            Detail = "TextureExpand gives a sprite a different image for states like drunk, "
                     + "asleep or hostile, and each of those is a separate file that mods "
                     + "argue over separately. You have settled the ordinary picture on "
                     + $"these and left {rows} of their conditions undecided, so the game "
                     + "will use your mod for the normal sprite and whatever load order "
                     + "picks for the rest.",
            Suggestion = "Settle the remaining conditions on each of these with the same "
                         + "mod you chose for the ordinary picture.",
        };

        finding.Evidence.AddRange(split.Take(16));
        if (split.Count > 16) finding.Evidence.Add($"... and {split.Count - 16} more");

        report.Findings.Add(finding);
    }

    /// <summary>
    /// Conflicts where every mod supplies byte-for-byte the same image.
    ///
    /// These count towards the conflict total and look like decisions waiting to be made,
    /// but there is nothing to decide: whichever mod wins, the picture in the game is the
    /// same. Mods pass art between each other constantly, so on a large library this is a
    /// real share of the number - and knowing which part of it is noise is the difference
    /// between a list worth working through and one nobody opens.
    /// </summary>
    private static void CheckIdenticalConflicts(ScanResult scan, HealthReport report)
    {
        var identical = new List<TextureEntry>();

        foreach (var entry in scan.Conflicts)
        {
            var hashes = entry.ModVersions
                .Select(v => v.Hash)
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // One distinct hash across every version, and a hash for each of them - a
            // missing hash would make two different files look like one.
            if (hashes.Count != 1) continue;
            if (entry.ModVersions.Any(v => string.IsNullOrEmpty(v.Hash))) continue;

            identical.Add(entry);
        }

        if (identical.Count == 0) return;

        var total = scan.ConflictCount;

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = SameImageCheck,
            Title = $"{identical.Count} of your {total} conflicts are the same image twice",
            Detail = "More than one mod supplies these, which is what makes them conflicts, "
                     + "but the files are byte for byte identical - so whichever one wins, "
                     + "the game looks the same. Mods pass art between each other, and this "
                     + "is what that looks like from the outside.",
            Suggestion = $"Nothing to decide on these. The {total - identical.Count} others "
                         + "are where the mods actually disagree.",
        };

        foreach (var entry in identical.Take(12))
        {
            var mods = entry.ModVersions.Select(v => v.ModName).Distinct().Take(3);
            finding.Evidence.Add($"{entry.DisplayId} — {string.Join(", ", mods)}");
        }

        if (identical.Count > 12) finding.Evidence.Add($"... and {identical.Count - 12} more");

        report.Findings.Add(finding);
    }

    private static List<string> FindWorkbooks(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.xlsx", SearchOption.AllDirectories)
                // Excel's own lock files, which are not workbooks.
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private static List<string> FindAssemblies(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Where(AssemblyIndex.IsManaged)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    // ---- 1. does every method a mod calls still exist? ----

    private static void CheckApi(ElinPaths paths,
        List<(ModPackage Mod, List<string> Dlls)> codeMods, HealthReport report)
    {
        if (!File.Exists(paths.GameAssembly))
        {
            report.GameAssemblyError = $"Game assembly not found at {paths.GameAssembly}.";
            return;
        }

        var defined = AssemblyIndex.Defined(paths.GameAssembly);
        report.GameMethodCount = defined.Count;

        if (defined.Count == 0)
        {
            report.GameAssemblyError = "The game assembly could not be read, so mods "
                                       + "cannot be checked against it.";
            return;
        }

        var exact = defined.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var byName = defined.GroupBy(m => $"{m.Type}.{m.Name}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Parameters).Distinct().ToList(),
                StringComparer.Ordinal);

        foreach (var (mod, dlls) in codeMods)
        {
            var bad = new List<string>();

            foreach (var dll in dlls)
            {
                foreach (var call in AssemblyIndex.Referenced(dll))
                {
                    var name = $"{call.Type}.{call.Name}";

                    // Only judge types the game actually declares; everything else
                    // belongs to some library this check knows nothing about.
                    if (!byName.TryGetValue(name, out var overloads)) continue;
                    if (exact.Contains(call.Key)) continue;

                    bad.Add($"{Path.GetFileName(dll)} calls {call}"
                            + $"  —  the game has {string.Join(" | ", overloads.Select(p => $"({p})"))}");
                }
            }

            if (bad.Count == 0) continue;

            var finding = new HealthFinding
            {
                Severity = HealthSeverity.Broken,
                Check = ApiCheck,
                Title = $"{mod.Name} calls a method this version of Elin no longer has",
                Detail = "The mod was built against an older Elin. When it reaches this "
                         + "call the game throws MissingMethodException and shows "
                         + "\"A mod is incompatible with your game version\". The error "
                         + "names the patches around the call, not this mod.",
                Suggestion = "Disable this mod, or update it if the author has published "
                             + "a build for the current game version.",
            };
            finding.ModKeys.Add(mod.Key);
            finding.ModNames.Add(mod.Name);
            finding.Evidence.AddRange(bad.Distinct().Take(8));
            report.Findings.Add(finding);
        }

        report.ScannedAssemblies = codeMods.Sum(c => c.Dlls.Count);
    }

    // ---- 2. is the same assembly installed twice? ----

    private static void CheckDuplicateAssemblies(
        List<(ModPackage Mod, List<string> Dlls)> codeMods, HealthReport report)
    {
        var byName = new Dictionary<string, List<(ModPackage Mod, string Path)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (mod, dlls) in codeMods)
        foreach (var dll in dlls)
        {
            var name = Path.GetFileName(dll);
            if (!byName.TryGetValue(name, out var list)) byName[name] = list = new();
            list.Add((mod, dll));
        }

        foreach (var (name, owners) in byName)
        {
            var mods = owners.Select(o => o.Mod).DistinctBy(m => m.Key).ToList();
            if (mods.Count < 2) continue;

            var finding = new HealthFinding
            {
                Severity = HealthSeverity.Broken,
                Check = DuplicateCheck,
                Title = $"{mods.Count} mods each ship {name}",
                Detail = "Two copies of the same assembly define the same types. Loading "
                         + "both is a TypeLoadException, usually reported as "
                         + "\"Failure has occurred while loading a type\".",
                Suggestion = "Keep one. Where one is a newer community fix of the other, "
                             + "keep the newer and disable the original.",
            };

            foreach (var m in mods) { finding.ModKeys.Add(m.Key); finding.ModNames.Add(m.Name); }
            foreach (var o in owners)
                finding.Evidence.Add($"{o.Mod.Name}  v{o.Mod.Version ?? "?"}  —  {o.Path}");

            report.Findings.Add(finding);
        }
    }

    // ---- 3. do two mods patch the same game method? ----

    private static void CheckPatchConflicts(
        List<(ModPackage Mod, List<string> Dlls)> codeMods, HealthReport report)
    {
        var byTarget = new Dictionary<string, List<ModPackage>>(StringComparer.Ordinal);

        foreach (var (mod, dlls) in codeMods)
        foreach (var dll in dlls)
        foreach (var target in AssemblyIndex.PatchTargets(dll))
        {
            // A bare type or method name is too vague to call a conflict on.
            if (!target.Contains('.')) continue;

            if (!byTarget.TryGetValue(target, out var list)) byTarget[target] = list = new();
            if (!list.Any(m => m.Key == mod.Key)) list.Add(mod);
        }

        var shared = byTarget.Where(p => p.Value.Count >= 2)
            .OrderByDescending(p => p.Value.Count)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();

        if (shared.Count == 0) return;

        // One finding, not one per method. Mods sharing a patch target is ordinary - a
        // real library has dozens - and raising each as its own alarm would bury the
        // findings that are actually broken. This is a place to look when a feature
        // stops working, not a list of faults.
        var f = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = PatchCheck,
            Title = $"{shared.Count} game methods are changed by more than one mod",
            Detail = "This is normal and usually harmless: the patches run in load order "
                     + "and most do not interfere. It matters when a mod's feature quietly "
                     + "does nothing, or when a mod warns you itself - Better Custom "
                     + "Sprites is one that does. The methods below are the crowded ones.",
            Suggestion = "Only act on this if you are seeing a problem in that area, and "
                         + "start with the most crowded method.",
        };

        foreach (var (target, mods) in shared.Take(12))
        {
            f.Evidence.Add($"{target}  —  {string.Join(", ", mods.Select(m => m.Name))}");
            foreach (var m in mods)
            {
                if (f.ModKeys.Contains(m.Key)) continue;
                f.ModKeys.Add(m.Key);
                f.ModNames.Add(m.Name);
            }
        }

        if (shared.Count > 12) f.Evidence.Add($"... and {shared.Count - 12} more");

        report.Findings.Add(f);
    }

    // ---- 4. is loadorder.txt in a state the game can act on? ----

    private void CheckLoadOrder(ElinPaths paths, ScanResult scan,
        LoadOrderDocument doc, HealthReport report)
    {
        if (!File.Exists(paths.LoadOrderFile))
        {
            report.Findings.Add(new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = LoadOrderCheck,
                Title = "Elin has not written a load order yet",
                Detail = "loadorder.txt does not exist. Every installed mod loads, and "
                         + "turning one off here creates the file.",
            });
            return;
        }

        // Paths whose letter case differs from disk. Windows opens them either way, so
        // the mod scans fine and looks right here - but the game matches on the string,
        // so a mod switched off would quietly stay switched on in play.
        var misCased = doc.Entries
            .Where(e => e.IsParsed)
            .Where(e => !string.Equals(SafePath.TrueCase(e.Path), e.Path, StringComparison.Ordinal))
            .Where(e => Directory.Exists(e.Path))
            .ToList();

        if (misCased.Count > 0)
        {
            var f = new HealthFinding
            {
                Severity = HealthSeverity.Broken,
                Check = LoadOrderCheck,
                Title = $"{misCased.Count} load-order lines are spelt differently from the folders on disk",
                Detail = "Elin finds each mod by comparing this path text, and the "
                         + "comparison is case-sensitive. A line it cannot match is a line "
                         + "it ignores, so these mods load whatever their on/off switch says.",
                Suggestion = "Apply any change on the Mods page - saving rewrites these "
                             + "lines in the spelling the game uses.",
            };
            foreach (var e in misCased.Take(8)) f.Evidence.Add(e.Path);
            report.Findings.Add(f);
        }

        // Entries pointing at folders that are gone: unsubscribed, but still listed.
        var orphans = doc.Entries.Where(e => e.IsParsed && !Directory.Exists(e.Path)).ToList();
        if (orphans.Count > 0)
        {
            var f = new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = LoadOrderCheck,
                Title = $"{orphans.Count} load-order entries point at folders that are gone",
                Detail = "These are mods you have unsubscribed from. They are harmless - "
                         + "the game skips them - but they make the list longer than it needs to be.",
            };

            foreach (var e in orphans.Take(8))
            {
                // The folder name is the Workshop ID, so a line that is only a path can
                // still be given a name - and once Steam has been asked, it can also say
                // whether the mod is still there to re-subscribe to.
                var id = Path.GetFileName(e.Path.TrimEnd(Path.DirectorySeparatorChar));
                var item = _workshop is not null && _workshop.TryGetValue(id, out var hit) ? hit : null;

                f.Evidence.Add(item is null
                    ? e.Path
                    : item.IsUnavailable
                        ? $"{item.Title ?? id} ({id}) - no longer on the Workshop"
                        : $"{item.Title ?? id} ({id}) - still on the Workshop, you unsubscribed");
            }

            report.Findings.Add(f);
        }

        var unparsed = doc.Entries.Where(e => !e.IsParsed).ToList();
        if (unparsed.Count > 0)
        {
            var f = new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = LoadOrderCheck,
                Title = $"{unparsed.Count} load-order lines are in a format this application does not recognise",
                Detail = "They are preserved exactly as they are and never rewritten, but "
                         + "their on/off state cannot be changed from here.",
            };
            foreach (var e in unparsed.Take(5)) f.Evidence.Add(e.Path);
            report.Findings.Add(f);
        }
    }

    // ---- 5. does every mod's dependency exist and is it switched on? ----

    private static void CheckDependencies(ElinPaths paths,
        List<(ModPackage Mod, List<string> Dlls)> codeMods, HealthReport report)
    {
        // Everything the game itself ships. Without this the check is nonsense: mods are
        // built against Plugins.BaseCore, Reflex and a hundred others that live in the
        // game's own Managed folder, and reading only mod folders reports every one of
        // them as a mod you failed to install.
        var stock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var managed = Path.GetDirectoryName(paths.GameAssembly);
            if (managed is not null && Directory.Exists(managed))
                foreach (var dll in Directory.GetFiles(managed, "*.dll"))
                    stock.Add(Path.GetFileNameWithoutExtension(dll));
        }
        catch { }

        // Who provides what. A mod's assembly file name is what other mods reference it
        // by, so this is the whole dependency graph Elin has - package.xml has no
        // dependency element at all, which is why the answer has to come from the code.
        var providers = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var (mod, dlls) in codeMods)
        foreach (var dll in dlls)
            providers.TryAdd(Path.GetFileNameWithoutExtension(dll), mod);

        foreach (var (mod, dlls) in codeMods)
        {
            var missing = new List<string>();
            var switchedOff = new List<(string Assembly, ModPackage Provider)>();

            foreach (var dll in dlls)
            {
                var own = Path.GetFileNameWithoutExtension(dll);

                foreach (var reference in AssemblyIndex.AssemblyRefs(dll))
                {
                    if (AssemblyIndex.IsAmbientAssembly(reference)) continue;
                    if (stock.Contains(reference)) continue;
                    if (string.Equals(reference, own, StringComparison.OrdinalIgnoreCase)) continue;

                    // A mod's own other assemblies are not a dependency on anyone else.
                    if (dlls.Any(d => string.Equals(
                            Path.GetFileNameWithoutExtension(d), reference,
                            StringComparison.OrdinalIgnoreCase))) continue;

                    if (!providers.TryGetValue(reference, out var provider))
                    {
                        if (!missing.Contains(reference)) missing.Add(reference);
                    }
                    else if (!provider.Enabled && provider.Key != mod.Key
                             && !switchedOff.Any(s => s.Provider.Key == provider.Key))
                    {
                        switchedOff.Add((reference, provider));
                    }
                }
            }

            if (missing.Count > 0)
            {
                var finding = new HealthFinding
                {
                    Severity = HealthSeverity.Broken,
                    Check = DependencyCheck,
                    Title = $"{mod.Name} needs a mod that is not installed",
                    Detail = "It is built against another mod's code. Without it the game "
                             + "cannot load this one's types, which surfaces as "
                             + "\"Failure has occurred while loading a type\" - naming "
                             + "neither mod.",
                    Suggestion = "Subscribe to the mod that provides it, or disable this one.",
                };
                finding.ModKeys.Add(mod.Key);
                finding.ModNames.Add(mod.Name);
                foreach (var m in missing.Take(6)) finding.Evidence.Add($"needs {m}.dll");
                report.Findings.Add(finding);
            }

            if (switchedOff.Count == 0 || !mod.Enabled) continue;

            var off = new HealthFinding
            {
                Severity = HealthSeverity.Broken,
                Check = DependencyCheck,
                Title = $"{mod.Name} needs a mod you have turned off",
                Detail = "The mod it is built against is installed but switched off in the "
                         + "load order, so its code will not be there when this one asks "
                         + "for it. Turning something off can break a mod you did not touch.",
                Suggestion = "Turn the other mod back on, or turn this one off as well.",
            };

            off.ModKeys.Add(mod.Key);
            off.ModNames.Add(mod.Name);
            foreach (var (assembly, provider) in switchedOff.Take(6))
                off.Evidence.Add($"needs {assembly}.dll, from {provider.Name} (off)");

            report.Findings.Add(off);
        }
    }

    // ---- 6. what does Steam currently publish for these mods? ----

    private void CheckWorkshop(ScanResult scan, HealthReport report)
    {
        if (_workshop is null) return;

        var waiting = new List<(ModPackage Mod, DateTime Published)>();
        var gone = new List<ModPackage>();

        foreach (var mod in scan.Mods.Where(m => m.SourceType == TextureSourceType.Workshop))
        {
            if (mod.WorkshopId is null) continue;
            if (!_workshop.TryGetValue(mod.WorkshopId, out var item)) continue;

            switch (WorkshopStatus.For(mod, item))
            {
                case WorkshopState.UpdateWaiting when item.TimeUpdatedUtc is { } published:
                    waiting.Add((mod, published));
                    break;
                case WorkshopState.Gone:
                    gone.Add(mod);
                    break;
            }
        }

        if (waiting.Count > 0)
        {
            var finding = new HealthFinding
            {
                Severity = HealthSeverity.Notice,
                Check = WorkshopCheck,
                Title = $"{waiting.Count} mods have a newer version on the Workshop",
                Detail = "The author published a change after your copy was last written. "
                         + "Steam usually picks these up on its own, but it only does so "
                         + "while it is running and not while the game is open.",
                Suggestion = "Restart Steam, or open each mod's page and let it re-download.",
            };

            foreach (var (mod, published) in waiting.OrderByDescending(w => w.Published))
            {
                finding.ModKeys.Add(mod.Key);
                finding.ModNames.Add(mod.Name);
                finding.Evidence.Add($"{mod.Name}  published {published:yyyy-MM-dd}, "
                                     + $"yours is from {mod.LastModifiedUtc:yyyy-MM-dd}");
            }

            report.Findings.Add(finding);
        }

        if (gone.Count == 0) return;

        var removed = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = WorkshopCheck,
            Title = $"{gone.Count} installed mods are no longer on the Workshop",
            Detail = "The page has been removed, hidden or banned. Your copy still works "
                     + "and will keep working, but it will never be updated again and "
                     + "cannot be re-downloaded if you unsubscribe.",
            Suggestion = "If you want to keep it, back the folder up before unsubscribing.",
        };

        foreach (var mod in gone)
        {
            removed.ModKeys.Add(mod.Key);
            removed.ModNames.Add(mod.Name);
            removed.Evidence.Add($"{mod.Name}  ({mod.WorkshopId})");
        }

        report.Findings.Add(removed);
    }

    // ---- 7. is a mod built for a much older Elin? ----

    private static void CheckVersions(ElinPaths paths,
        List<(ModPackage Mod, List<string> Dlls)> codeMods, HealthReport report)
    {
        var gameVersion = ReadGameVersion(paths);
        if (gameVersion is null) return;

        var behind = new List<(ModPackage Mod, Version V)>();

        foreach (var (mod, _) in codeMods)
        {
            if (!TryParseVersion(mod.Version, out var v)) continue;

            // Only the minor line matters: mods track the Elin build they were made for,
            // and a whole minor version behind is where the breaking changes live.
            if (v.Major < gameVersion.Major || (v.Major == gameVersion.Major && v.Minor < gameVersion.Minor))
                behind.Add((mod, v));
        }

        if (behind.Count == 0) return;

        var finding = new HealthFinding
        {
            Severity = HealthSeverity.Notice,
            Check = VersionCheck,
            Title = $"{behind.Count} mods that ship code target an older Elin than you are running",
            Detail = $"You are on {gameVersion}. These declare an older build. That is a "
                     + "hint, not a verdict - plenty of mods keep working for versions, and "
                     + "authors do not always bump the number. The Game API check above is "
                     + "the one that proves breakage.",
        };

        foreach (var (mod, v) in behind.OrderBy(b => b.V))
        {
            finding.ModKeys.Add(mod.Key);
            finding.ModNames.Add(mod.Name);
            finding.Evidence.Add($"{mod.Name}  declares {v}");
        }

        report.Findings.Add(finding);
    }

    private static Version? ReadGameVersion(ElinPaths paths)
    {
        // The modding kit ships with the game and tracks its build.
        var kit = Path.Combine(paths.PackageRoot, "_ModdingKit", "package.xml");
        try
        {
            if (!File.Exists(kit)) return null;
            var xml = System.Xml.Linq.XDocument.Load(kit);
            var raw = xml.Root?.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "version",
                    StringComparison.OrdinalIgnoreCase))?.Value;
            return TryParseVersion(raw, out var v) ? v : null;
        }
        catch { return null; }
    }

    private static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = new string(raw.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(cleaned, out version!) && version is not null;
    }
}
