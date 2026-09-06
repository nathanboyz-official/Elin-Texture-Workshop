using System.Text;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.Core.ImportExport;

public sealed record ImportOutcome(
    int Restored,
    List<OverrideSelection> Missing,
    string Message);

/// <summary>
/// Import and export of texture selections, so a setup can be backed up, moved to
/// another machine or shared.
/// </summary>
public static class SelectionTransfer
{
    /// <summary>Writes the active profile's selections to a JSON file.</summary>
    public static bool Export(SelectionStore selections, string targetFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(targetFile, selections.ExportJson(), new UTF8Encoding(false));
            AppLog.Info($"Exported {selections.Count} selections to {targetFile}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not export selections to {targetFile}", ex);
            return false;
        }
    }

    /// <summary>
    /// Restores selections from an export. Entries whose source mod is installed are
    /// re-applied; the rest are returned so the UI can show exactly what is missing.
    /// </summary>
    public static ImportOutcome Import(
        string sourceFile,
        ScanResult scan,
        OverrideManager overrides)
    {
        var missing = new List<OverrideSelection>();

        string json;
        try { json = File.ReadAllText(sourceFile); }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read {sourceFile}", ex);
            return new ImportOutcome(0, missing, $"Could not read the file: {ex.Message}");
        }

        var export = SelectionStore.ParseExport(json);
        if (export is null || export.Selections.Count == 0)
            return new ImportOutcome(0, missing, "That file contained no selections.");

        var byKey = scan.Mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        var restored = 0;

        foreach (var sel in export.Selections)
        {
            var source = FindSource(sel, byKey, scan);

            if (source is null)
            {
                missing.Add(sel);
                continue;
            }

            var result = overrides.Select(source);
            if (result.Success) restored++;
            else missing.Add(sel);
        }

        var message = missing.Count == 0
            ? $"Restored all {restored} selections."
            : $"Restored {restored} selections; {missing.Count} could not be matched to an installed mod.";

        AppLog.Info($"Selection import: {message}");
        return new ImportOutcome(restored, missing, message);
    }

    /// <summary>
    /// Finds the file an exported selection refers to: first the exact mod, then any
    /// installed mod carrying a byte-identical copy of the same texture.
    /// </summary>
    private static TextureFile? FindSource(
        OverrideSelection sel,
        IReadOnlyDictionary<string, ModPackage> byKey,
        ScanResult scan)
    {
        if (byKey.TryGetValue(sel.SourceModKey, out var mod))
        {
            var exact = mod.Textures.FirstOrDefault(t =>
                string.Equals(t.FileName, sel.FileName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        if (sel.SourceHash is null) return null;

        // The mod may have been renamed or re-uploaded; an identical file elsewhere is
        // the same texture as far as the user is concerned.
        if (scan.Index.TryGetValue(sel.TextureId, out var entry))
        {
            return entry.AllSources.FirstOrDefault(v =>
                string.Equals(v.Hash, sel.SourceHash, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// Copies the override package to a folder of the user's choosing, producing a
    /// self-contained Elin mod that can be backed up or shared.
    /// </summary>
    public static bool ExportAsMod(ElinPaths paths, string targetDirectory, string modTitle)
    {
        try
        {
            var textureTarget = Path.Combine(targetDirectory, ElinPaths.TextureReplaceFolder);
            Directory.CreateDirectory(textureTarget);

            var copied = 0;
            if (Directory.Exists(paths.OverrideTextureRoot))
            {
                foreach (var file in Directory.GetFiles(paths.OverrideTextureRoot, "*.png"))
                {
                    File.Copy(file, Path.Combine(textureTarget, Path.GetFileName(file)), overwrite: true);
                    copied++;
                }
            }

            var xml = PackageMetadataParser.BuildOverridePackageXml(
                modTitle,
                "elintexturemanager.export." + Guid.NewGuid().ToString("N")[..8],
                OverrideManager.DefaultLoadPriority);

            File.WriteAllText(Path.Combine(targetDirectory, "package.xml"), xml, new UTF8Encoding(false));

            AppLog.Info($"Exported override mod ({copied} textures) to {targetDirectory}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not export the override mod to {targetDirectory}", ex);
            return false;
        }
    }
}
