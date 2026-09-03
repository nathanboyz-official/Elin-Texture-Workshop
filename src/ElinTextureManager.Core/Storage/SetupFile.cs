using System.Text.Json;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Storage;

/// <summary>One mod, as a setup file records it.</summary>
public sealed class SetupMod
{
    public string? WorkshopId { get; set; }

    /// <summary>Kept for display when the ID is not subscribed on the machine importing it.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// A whole setup: which mods are on, and which mod wins each contested texture.
///
/// Deliberately not a copy of anything. It records Workshop IDs and texture IDs, so it
/// is a few kilobytes of text that says what to do rather than a redistribution of
/// other people's art - importing it subscribes to nothing and downloads nothing, and
/// tells you plainly what is missing.
/// </summary>
public sealed class SetupDocument
{
    public string Application { get; set; } = "Elin Texture Workshop";
    public int FormatVersion { get; set; } = 1;
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

    public string Profile { get; set; } = "Default";
    public string? Note { get; set; }

    public List<SetupMod> Mods { get; set; } = new();
    public List<OverrideSelection> Selections { get; set; } = new();
}

/// <summary>What importing a setup would do, or did.</summary>
public sealed class SetupImportReport
{
    public string Profile { get; set; } = "Default";

    /// <summary>Mods the setup names that are not on this machine, by name.</summary>
    public List<string> MissingMods { get; } = new();

    /// <summary>Mods whose on/off state this changed.</summary>
    public List<string> Toggled { get; } = new();

    /// <summary>Selections whose source mod is here, so they can be applied.</summary>
    public List<OverrideSelection> Applicable { get; } = new();

    /// <summary>Selections whose source mod is not here, named for the message.</summary>
    public List<string> UnavailableSelections { get; } = new();

    public bool IsComplete => MissingMods.Count == 0 && UnavailableSelections.Count == 0;
}

public static class SetupFile
{
    /// <summary>Builds a setup document from the current library and the active profile.</summary>
    public static SetupDocument Build(IEnumerable<ModPackage> mods,
        IEnumerable<OverrideSelection> selections, string profile, string? note = null)
    {
        var document = new SetupDocument { Profile = profile, Note = note };

        foreach (var mod in mods.Where(m => m.SourceType == TextureSourceType.Workshop)
                     .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            document.Mods.Add(new SetupMod
            {
                WorkshopId = mod.WorkshopId,
                Name = mod.Name,
                Enabled = mod.Enabled,
            });
        }

        document.Selections.AddRange(selections
            .OrderBy(s => s.TextureId, StringComparer.OrdinalIgnoreCase));

        return document;
    }

    public static string Write(SetupDocument document) =>
        JsonSerializer.Serialize(document, SelectionStore.JsonOptions);

    public static SetupDocument? Read(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<SetupDocument>(json, SelectionStore.JsonOptions);
            if (document is null) return null;

            // A file with neither half is not a setup, whatever it claims to be.
            return document.Mods.Count == 0 && document.Selections.Count == 0 ? null : document;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not read the setup file", ex);
            return null;
        }
    }

    /// <summary>
    /// Works out what a setup would do here, without doing any of it. Nothing is
    /// subscribed and nothing is downloaded: the answer to a mod that is not installed
    /// is to say so, not to go and get it.
    /// </summary>
    public static SetupImportReport Plan(SetupDocument document, IReadOnlyList<ModPackage> installed)
    {
        var report = new SetupImportReport { Profile = document.Profile };

        var byWorkshopId = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        var byKey = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installed)
        {
            if (!string.IsNullOrEmpty(mod.WorkshopId)) byWorkshopId.TryAdd(mod.WorkshopId, mod);
            byKey.TryAdd(mod.Key, mod);
        }

        foreach (var wanted in document.Mods)
        {
            var mod = wanted.WorkshopId is not null
                      && byWorkshopId.TryGetValue(wanted.WorkshopId, out var hit) ? hit : null;

            if (mod is null)
            {
                var label = string.IsNullOrEmpty(wanted.WorkshopId)
                    ? wanted.Name
                    : $"{wanted.Name} ({wanted.WorkshopId})";
                report.MissingMods.Add(label);
                continue;
            }

            if (mod.Enabled != wanted.Enabled && mod.CanToggle) report.Toggled.Add(mod.Name);
        }

        foreach (var selection in document.Selections)
        {
            var available = (selection.SourceWorkshopId is not null
                             && byWorkshopId.ContainsKey(selection.SourceWorkshopId))
                            || byKey.ContainsKey(selection.SourceModKey);

            if (available) report.Applicable.Add(selection);
            else report.UnavailableSelections.Add($"{selection.TextureId} from {selection.SourceModName}");
        }

        return report;
    }

    /// <summary>The on/off changes a plan implies, ready to hand to the load-order writer.</summary>
    public static List<(ModPackage Mod, bool Enabled)> EnabledChanges(
        SetupDocument document, IReadOnlyList<ModPackage> installed)
    {
        var byWorkshopId = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in installed)
            if (!string.IsNullOrEmpty(mod.WorkshopId)) byWorkshopId.TryAdd(mod.WorkshopId, mod);

        var changes = new List<(ModPackage, bool)>();

        foreach (var wanted in document.Mods)
        {
            if (wanted.WorkshopId is null) continue;
            if (!byWorkshopId.TryGetValue(wanted.WorkshopId, out var mod)) continue;
            if (!mod.CanToggle || mod.Enabled == wanted.Enabled) continue;

            changes.Add((mod, wanted.Enabled));
        }

        return changes;
    }
}
