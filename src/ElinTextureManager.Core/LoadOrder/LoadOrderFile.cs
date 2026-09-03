using System.Text;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;

namespace ElinTextureManager.Core.LoadOrder;

/// <summary>
/// Reads and writes Elin's loadorder.txt.
///
/// Format confirmed from the live installation - one line per mod:
///     C:\...\steamapps\workshop\content\2135150\3427330411,1
/// where the trailing field is 1 for enabled and 0 for disabled. The file lists
/// Workshop items only; packages under Elin\Package are not present, which is why
/// the override package relies on loadPriority rather than a load-order entry.
/// </summary>
public sealed class LoadOrderDocument
{
    public List<LoadOrderEntry> Entries { get; } = new();

    /// <summary>Path the document was read from.</summary>
    public string? FilePath { get; init; }

    public bool Exists => FilePath is not null && File.Exists(FilePath);

    public int IndexOfPath(string path)
    {
        var normalized = SafePathCompare(path);
        for (var i = 0; i < Entries.Count; i++)
            if (SafePathCompare(Entries[i].Path) == normalized) return i;
        return -1;
    }

    private static string SafePathCompare(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return p.Trim().TrimEnd('\\', '/').ToLowerInvariant(); }
    }
}

public static class LoadOrderFile
{
    /// <summary>
    /// Parses loadorder.txt. Unrecognised lines are preserved verbatim so that saving
    /// can never silently discard something we did not understand.
    /// </summary>
    public static LoadOrderDocument Read(string path)
    {
        var doc = new LoadOrderDocument { FilePath = path };

        try
        {
            if (!File.Exists(path))
            {
                AppLog.Warn($"loadorder.txt not found at {path}.");
                return doc;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var line = raw.TrimEnd();
                var comma = line.LastIndexOf(',');

                if (comma > 0 && comma == line.Length - 2 &&
                    (line[^1] == '0' || line[^1] == '1'))
                {
                    doc.Entries.Add(new LoadOrderEntry
                    {
                        Path = line[..comma],
                        Enabled = line[^1] == '1',
                    });
                }
                else
                {
                    // Keep it, flagged as unparsed, rather than dropping the user's data.
                    doc.Entries.Add(new LoadOrderEntry
                    {
                        Path = line,
                        Enabled = true,
                        RawLine = line,
                    });
                    AppLog.Warn($"Unrecognised loadorder.txt line preserved verbatim: {line}");
                }
            }

            AppLog.Info($"Read loadorder.txt: {doc.Entries.Count} entries "
                        + $"({doc.Entries.Count(e => e.Enabled)} enabled).");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to read loadorder.txt at {path}", ex);
        }

        return doc;
    }

    /// <summary>
    /// Copies loadorder.txt to the backup folder. Returns the backup path, or null when
    /// there was nothing to back up. Saving must never proceed without this succeeding.
    /// </summary>
    public static string? Backup(string loadOrderPath, string backupDirectory)
    {
        try
        {
            if (!File.Exists(loadOrderPath)) return null;

            Directory.CreateDirectory(backupDirectory);
            var name = $"loadorder.backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt";
            var target = Path.Combine(backupDirectory, name);

            // Never clobber an existing backup.
            var n = 1;
            while (File.Exists(target))
                target = Path.Combine(backupDirectory,
                    $"loadorder.backup_{DateTime.Now:yyyy-MM-dd_HHmmss}_{n++}.txt");

            File.Copy(loadOrderPath, target);
            AppLog.Info($"Load order backed up to {target}");
            return target;
        }
        catch (Exception ex)
        {
            AppLog.Error("Load order backup failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Writes loadorder.txt, taking a backup first. The write goes to a temporary file
    /// and is then moved into place, so an interrupted write cannot truncate the original.
    /// </summary>
    public static bool Save(LoadOrderDocument doc, string backupDirectory, out string? backupPath)
    {
        backupPath = null;
        var path = doc.FilePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            AppLog.Error("Cannot save load order: no file path.");
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                backupPath = Backup(path, backupDirectory);
                if (backupPath is null)
                {
                    AppLog.Error("Refusing to save load order because the backup failed.");
                    return false;
                }
            }

            // Repair any entry whose path differs from the on-disk spelling only by
            // case. Elin compares these strings to find the mod, and a line it cannot
            // match is a line it ignores - so a mod disabled here would quietly stay
            // switched on in the game. Windows opens either spelling, which is exactly
            // why the mismatch is invisible until you are standing in front of the NPC
            // you thought you had removed. Unparsed lines are never touched.
            foreach (var e in doc.Entries)
            {
                if (!e.IsParsed) continue;

                var onDisk = SafePath.TrueCase(e.Path);
                if (string.Equals(onDisk, e.Path, StringComparison.Ordinal)) continue;

                AppLog.Info($"Rewriting load-order path to its on-disk spelling: {e.Path} -> {onDisk}");
                e.Path = onDisk;
            }

            var text = new StringBuilder();
            foreach (var e in doc.Entries) text.Append(e.Serialize()).Append("\r\n");

            var temp = path + ".etm_tmp";
            // Match the original file: UTF-8 without a BOM.
            File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);

            AppLog.Info($"Load order saved: {doc.Entries.Count} entries.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save load order to {path}", ex);
            return false;
        }
    }

    /// <summary>
    /// Sets one mod's enabled flag. Returns true when the document actually changed, so a
    /// caller can avoid writing (and backing up) the file for a no-op.
    ///
    /// A mod Steam has downloaded but Elin has not launched with yet is absent from the
    /// file. Absent means loaded, so enabling such a mod is already true and needs no
    /// entry; disabling one does, and appends a line the game reads on next launch.
    /// </summary>
    public static bool SetEnabled(LoadOrderDocument doc, string modDirectory, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(modDirectory)) return false;

        var index = doc.IndexOfPath(modDirectory);

        if (index >= 0)
        {
            var entry = doc.Entries[index];

            // An unparsed line is preserved verbatim on save, so its flag is not ours to
            // change; refuse rather than write something the game may not understand.
            if (!entry.IsParsed)
            {
                AppLog.Warn($"Refusing to change the enabled flag of an unparsed "
                            + $"loadorder.txt line: {entry.Path}");
                return false;
            }

            if (entry.Enabled == enabled) return false;

            entry.Enabled = enabled;
            return true;
        }

        if (enabled) return false;

        string full;
        try { full = SafePath.TrueCase(modDirectory); }
        catch (Exception ex)
        {
            AppLog.Warn($"Cannot add a load-order entry for '{modDirectory}': {ex.Message}");
            return false;
        }

        doc.Entries.Add(new LoadOrderEntry { Path = full, Enabled = false });
        AppLog.Info($"Added a disabled load-order entry for {full}");
        return true;
    }

    /// <summary>Lists available load-order backups, newest first.</summary>
    public static IReadOnlyList<FileInfo> ListBackups(string backupDirectory)
    {
        try
        {
            if (!Directory.Exists(backupDirectory)) return Array.Empty<FileInfo>();
            return new DirectoryInfo(backupDirectory)
                .GetFiles("loadorder.backup_*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not list load order backups", ex);
            return Array.Empty<FileInfo>();
        }
    }

    /// <summary>
    /// Restores a backup over loadorder.txt. The current file is itself backed up first,
    /// so a restore is always reversible.
    /// </summary>
    public static bool Restore(string backupFile, ElinPaths paths, string backupDirectory)
    {
        try
        {
            if (!File.Exists(backupFile))
            {
                AppLog.Error($"Backup not found: {backupFile}");
                return false;
            }

            if (!SafePath.IsInside(backupFile, backupDirectory))
            {
                AppLog.Warn($"Refusing to restore from outside the backup folder: {backupFile}");
                return false;
            }

            Backup(paths.LoadOrderFile, backupDirectory);
            File.Copy(backupFile, paths.LoadOrderFile, overwrite: true);
            AppLog.Info($"Load order restored from {backupFile}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to restore load order from {backupFile}", ex);
            return false;
        }
    }

    /// <summary>
    /// Applies load-order position and enabled state onto scanned mods.
    /// Mods absent from the file (local packages) keep LoadOrderIndex = -1.
    /// </summary>
    public static void ApplyTo(LoadOrderDocument doc, IEnumerable<ModPackage> mods)
    {
        var byPath = new Dictionary<string, (int index, bool enabled)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < doc.Entries.Count; i++)
        {
            var e = doc.Entries[i];
            var key = SafePath.Normalize(e.Path);
            if (key is not null) byPath[key] = (i, e.Enabled);
        }

        foreach (var mod in mods)
        {
            var key = SafePath.Normalize(mod.Directory);
            if (key is not null && byPath.TryGetValue(key, out var hit))
            {
                mod.LoadOrderIndex = hit.index;
                mod.Enabled = hit.enabled;
                mod.InLoadOrderFile = true;
            }
            else
            {
                mod.LoadOrderIndex = -1;
                mod.InLoadOrderFile = false;
                mod.Enabled = true; // Local packages are always loaded.
            }
        }
    }
}
