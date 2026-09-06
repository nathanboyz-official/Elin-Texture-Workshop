using System.Text;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.Core.Overrides;

public sealed record OverrideResult(bool Success, string Message)
{
    public static OverrideResult Ok(string message = "Done") => new(true, message);
    public static OverrideResult Fail(string message) => new(false, message);
}

/// <summary>
/// Owns the override package: Elin\Package\ElinTextureManager_Overrides.
///
/// Every selected texture is COPIED here from its source mod. Workshop folders are
/// only ever read, and deletes are confined to this package by <see cref="SafePath"/>.
/// </summary>
public sealed class OverrideManager
{
    private readonly ElinPaths _paths;
    private readonly SelectionStore _selections;

    /// <summary>
    /// loadPriority written into the override package, so it loads after everything and
    /// the user's chosen texture is the one the game ends up with.
    ///
    /// Exactly the game's maximum, not more. This was 1000 - one past the limit - and the
    /// game clamps with Mathf.Clamp(result, -999, 999), so the package that has to win
    /// was silently landing on 999 and tying with whatever else asked for too much. On
    /// one real install that was three other mods, one of them NJYMTextureExpand, whose
    /// own reason for sitting at 999 is to load last as well. Ties are broken by nothing
    /// in particular, so the whole point of this package was left to chance.
    /// </summary>
    public const int DefaultLoadPriority = Model.PackageLimits.MaxLoadPriority;

    public OverrideManager(ElinPaths paths, SelectionStore selections)
    {
        _paths = paths;
        _selections = selections;
    }

    public string PackageRoot => _paths.OverridePackageRoot;
    public string TextureRoot => _paths.OverrideTextureRoot;
    public string PortraitRoot => _paths.OverridePortraitRoot;

    /// <summary>
    /// Optional second copy into Elin\User\Texture Replace - the game's own user-level
    /// replacement folder. Off by default; a fallback if package overrides do not apply.
    /// </summary>
    public bool MirrorToUserFolder { get; set; }

    /// <summary>Creates the package folder and package.xml if they are not there yet.</summary>
    public OverrideResult EnsurePackage(int loadPriority = DefaultLoadPriority)
    {
        try
        {
            Directory.CreateDirectory(_paths.OverridePackageRoot);
            foreach (var kind in ReplacementKindExtensions.All)
                Directory.CreateDirectory(_paths.OverrideRootFor(kind));

            if (!File.Exists(_paths.OverridePackageXml))
            {
                var xml = PackageMetadataParser.BuildOverridePackageXml(
                    "Elin Texture Manager Overrides",
                    "elintexturemanager.overrides",
                    loadPriority);

                File.WriteAllText(_paths.OverridePackageXml, xml, new UTF8Encoding(false));
                AppLog.Info($"Created override package at {_paths.OverridePackageRoot}");
            }

            return OverrideResult.Ok("Override package ready.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not create the override package", ex);
            return OverrideResult.Fail($"Could not create the override package: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies a chosen texture into the override package and records the selection.
    /// The source file is never modified.
    /// </summary>
    public OverrideResult Select(TextureFile source)
    {
        if (!SafePath.IsSafeFileName(source.FileName))
            return OverrideResult.Fail($"Unsafe texture file name: '{source.FileName}'.");

        var ready = EnsurePackage();
        if (!ready.Success) return ready;

        // A package mirrors _Elona's layout, so a portrait override has to land in the
        // package's Portrait folder - putting it in "Texture Replace" would do nothing.
        var target = Path.Combine(_paths.OverrideRootFor(source.Kind), source.FileName);

        var verdict = SafePath.Audit("Write override", target,
            SafePath.CanWriteOverrideFile(target, _paths));
        if (!verdict.Allowed)
            return OverrideResult.Fail($"Refused to write override: {verdict.Reason}");

        try
        {
            if (!File.Exists(source.FullPath))
                return OverrideResult.Fail("The source texture no longer exists on disk.");

            File.Copy(source.FullPath, target, overwrite: true);

            // Copies inherit the source's read-only flag from Workshop folders; clear it
            // so the file can be replaced or removed later.
            ClearReadOnly(target);

            // The user-level fallback folder only exists for Texture Replace; Elin has no
            // User\Portrait equivalent, so portraits are never mirrored.
            if (MirrorToUserFolder && source.Kind == ReplacementKind.TextureReplace)
                MirrorToUser(source);

            _selections.Set(new OverrideSelection
            {
                TextureId = source.TextureId,
                FileName = source.FileName,
                SourceModKey = source.ModKey,
                SourceWorkshopId = source.WorkshopId,
                SourceModName = source.ModName,
                SourcePath = source.FullPath,
                SourceHash = source.Hash ?? ImageInfo.TryComputeHash(source.FullPath),
                SourceType = source.SourceType,
                Kind = source.Kind,
                SelectedUtc = DateTime.UtcNow,
            });

            _selections.Save();

            AppLog.Info($"Override set: {source.TextureId} <- {source.ModName} ({source.WorkshopId})");
            return OverrideResult.Ok($"{source.TextureId} now uses {source.ModName}.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to copy override for {source.TextureId}", ex);
            return OverrideResult.Fail($"Could not copy the texture: {ex.Message}");
        }
    }

    private void MirrorToUser(TextureFile source)
    {
        try
        {
            Directory.CreateDirectory(_paths.UserTextureReplace);
            var target = Path.Combine(_paths.UserTextureReplace, source.FileName);
            File.Copy(source.FullPath, target, overwrite: true);
            ClearReadOnly(target);
            AppLog.Info($"Mirrored {source.FileName} to Elin\\User\\Texture Replace.");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not mirror {source.FileName} to the user folder: {ex.Message}");
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch { }
    }

    /// <summary>
    /// Removes one override. Only the copied file inside the override package is deleted;
    /// the source mod is untouched, and Elin falls back to its load-order winner.
    /// </summary>
    public OverrideResult Remove(string textureId)
    {
        var selection = _selections.Get(textureId);
        var fileName = selection?.FileName;

        if (fileName is null)
        {
            // No record, but a stray file may still exist: fall back to the ID plus .png.
            // The ID may carry the portrait namespace, which is never part of a file name.
            fileName = TextureIdentity.Display(textureId) + ".png";
        }

        if (!SafePath.IsSafeFileName(fileName))
            return OverrideResult.Fail($"Unsafe file name recorded for {textureId}.");

        // Selections written before portrait support have no kind; the namespace on the
        // ID still identifies a portrait, so the file is looked for in the right folder.
        var kind = selection?.Kind
                   ?? (TextureIdentity.IsPortraitId(textureId)
                       ? ReplacementKind.Portrait
                       : ReplacementKind.TextureReplace);

        var target = Path.Combine(_paths.OverrideRootFor(kind), fileName);

        if (!File.Exists(target))
        {
            _selections.RemoveAndSave(textureId);
            return OverrideResult.Ok("Override entry cleared (no file was present).");
        }

        var verdict = SafePath.Audit("Delete override", target,
            SafePath.CanDeleteOverrideFile(target, _paths, fileName));

        if (!verdict.Allowed)
            return OverrideResult.Fail($"Refused to delete: {verdict.Reason}");

        try
        {
            ClearReadOnly(target);
            File.Delete(target);
            if (kind == ReplacementKind.TextureReplace) RemoveMirror(fileName);
            _selections.RemoveAndSave(textureId);

            AppLog.Info($"Override removed: {textureId}");
            return OverrideResult.Ok($"Override for {textureId} removed.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to remove override for {textureId}", ex);
            return OverrideResult.Fail($"Could not delete the override file: {ex.Message}");
        }
    }

    private void RemoveMirror(string fileName)
    {
        try
        {
            var mirror = Path.Combine(_paths.UserTextureReplace, fileName);
            if (!File.Exists(mirror)) return;

            // Confined to Elin\User\Texture Replace and matched by name.
            if (!SafePath.IsInside(mirror, _paths.UserTextureReplace)) return;

            ClearReadOnly(mirror);
            File.Delete(mirror);
            AppLog.Info($"Removed mirrored override {fileName} from the user folder.");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not remove mirrored file {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes every override. Returns how many files were deleted. Each delete is
    /// validated individually - this never recursively wipes a folder.
    /// </summary>
    public (int Removed, int Failed) ClearAll()
    {
        var removed = 0;
        var failed = 0;

        foreach (var selection in _selections.All().ToList())
        {
            var result = Remove(selection.TextureId);
            if (result.Success) removed++;
            else failed++;
        }

        // Sweep any orphaned PNGs that no longer have a selection record.
        try
        {
            foreach (var kind in ReplacementKindExtensions.All)
            {
                var root = _paths.OverrideRootFor(kind);
                if (!Directory.Exists(root)) continue;

                foreach (var file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    var verdict = SafePath.CanDeleteOverrideFile(file, _paths, Path.GetFileName(file));
                    if (!verdict.Allowed) { failed++; continue; }

                    try { ClearReadOnly(file); File.Delete(file); removed++; }
                    catch { failed++; }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Sweep of orphaned override files failed", ex);
        }

        _selections.Save();
        AppLog.Info($"Cleared overrides: {removed} removed, {failed} failed.");
        return (removed, failed);
    }

    /// <summary>
    /// Re-checks each selection against the mods currently installed, so the UI can show
    /// "source updated" and "source mod no longer installed" without guessing.
    /// </summary>
    public IReadOnlyList<OverrideStatus> Audit(ScanResult scan)
    {
        var byKey = scan.Mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        var list = new List<OverrideStatus>();

        foreach (var sel in _selections.All())
        {
            var overrideFile = Path.Combine(_paths.OverrideRootFor(sel.Kind), sel.FileName);
            var fileExists = File.Exists(overrideFile);

            if (!byKey.TryGetValue(sel.SourceModKey, out var mod))
            {
                list.Add(new OverrideStatus(sel, OverrideState.SourceMissing, fileExists, null));
                continue;
            }

            var current = mod.Textures.FirstOrDefault(t =>
                string.Equals(t.FileName, sel.FileName, StringComparison.OrdinalIgnoreCase) &&
                !t.IsVariant);

            current ??= mod.Textures.FirstOrDefault(t =>
                string.Equals(t.FileName, sel.FileName, StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                list.Add(new OverrideStatus(sel, OverrideState.SourceMissing, fileExists, null));
                continue;
            }

            var changed = sel.SourceHash is not null
                          && current.Hash is not null
                          && !string.Equals(sel.SourceHash, current.Hash, StringComparison.OrdinalIgnoreCase);

            var state = !fileExists ? OverrideState.FileMissing
                : changed ? OverrideState.SourceUpdated
                : OverrideState.Ok;

            list.Add(new OverrideStatus(sel, state, fileExists, current));
        }

        return list;
    }
}

public enum OverrideState
{
    Ok,
    /// <summary>Steam updated the mod and the source texture's bytes changed.</summary>
    SourceUpdated,
    /// <summary>The mod that supplied this texture is no longer installed.</summary>
    SourceMissing,
    /// <summary>The selection is recorded but the copied file has gone.</summary>
    FileMissing,
}

public sealed record OverrideStatus(
    OverrideSelection Selection,
    OverrideState State,
    bool FileExists,
    TextureFile? CurrentSource);
