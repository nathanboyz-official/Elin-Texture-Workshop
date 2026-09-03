using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Overrides;

public sealed record SafetyVerdict(bool Allowed, string Reason)
{
    public static SafetyVerdict Ok { get; } = new(true, "ok");
    public static SafetyVerdict Deny(string reason) => new(false, reason);
}

/// <summary>
/// Guards every destructive file operation. The application only ever deletes files
/// inside its own override package, so each delete has to prove it belongs there.
/// Every check is fail-closed: anything unexpected returns a denial.
/// </summary>
public static class SafePath
{
    /// <summary>
    /// The path exactly as Windows stores it on disk, letter case included.
    ///
    /// This matters because loadorder.txt is read back by Elin, and a line the game
    /// cannot match is a line it ignores. Windows itself is case-insensitive, so a
    /// path that merely differs in case still opens and still scans - which is why a
    /// mismatch here is invisible everywhere except in the game's own behaviour.
    /// Writing the same form the game writes removes the question entirely.
    ///
    /// Never throws: anything unresolvable comes back as it went in.
    /// </summary>
    public static string TrueCase(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return full;

            // Windows writes the drive letter upper case; match it.
            var result = root.ToUpperInvariant();

            var segments = full[root.Length..]
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(s => s.Length > 0);

            foreach (var segment in segments)
            {
                string[] hits;
                try { hits = Directory.GetFileSystemEntries(result, segment); }
                catch { return full; }

                // Not on disk (yet): keep the caller's spelling for the remainder.
                result = hits.Length == 1 ? hits[0] : Path.Combine(result, segment);
            }

            return result;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>Normalises a path for comparison: absolute, no trailing separator.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return null; }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is the same as, or nested inside,
    /// <paramref name="root"/>. Uses normalised paths so "..\" cannot escape.
    /// </summary>
    public static bool IsInside(string? candidate, string? root)
    {
        var c = Normalize(candidate);
        var r = Normalize(root);
        if (c is null || r is null) return false;

        if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return true;

        return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decides whether a file may be deleted. Mirrors the safety rules exactly:
    /// the path must exist, be a file, live under the override texture folder, carry
    /// the expected file name, and not touch Workshop or base-game directories.
    /// </summary>
    public static SafetyVerdict CanDeleteOverrideFile(
        string filePath,
        ElinPaths paths,
        string? expectedFileName = null)
    {
        var target = Normalize(filePath);
        if (target is null) return SafetyVerdict.Deny("Path is empty or malformed.");

        if (Directory.Exists(target))
            return SafetyVerdict.Deny("Refusing to delete a directory; only files are removable.");

        if (!File.Exists(target))
            return SafetyVerdict.Deny("File does not exist.");

        // Confined to the replacement folders of our own package. Everything else in the
        // package root - package.xml above all - is deliberately not deletable.
        var roots = Model.ReplacementKindExtensions.All
            .Select(k => Normalize(paths.OverrideRootFor(k)))
            .Where(r => r is not null)
            .ToList();

        if (roots.Count == 0)
            return SafetyVerdict.Deny("Override folder is not configured.");

        if (!roots.Any(r => IsInside(target, r)))
            return SafetyVerdict.Deny("Path is outside the override replacement folders.");

        // The override folder is inside the Elin install, so the two checks below are
        // belt-and-braces against a mis-configured override path.
        if (!string.IsNullOrWhiteSpace(paths.WorkshopRoot) && IsInside(target, paths.WorkshopRoot))
            return SafetyVerdict.Deny("Path is inside the Steam Workshop folder.");

        if (IsInside(target, Path.Combine(paths.ElinRoot, "Elin_Data")))
            return SafetyVerdict.Deny("Path is inside the base game asset folder.");

        if (expectedFileName is not null &&
            !string.Equals(Path.GetFileName(target), expectedFileName, StringComparison.OrdinalIgnoreCase))
            return SafetyVerdict.Deny($"File name does not match the expected '{expectedFileName}'.");

        return SafetyVerdict.Ok;
    }

    /// <summary>
    /// Decides whether a file may be written. Writes are confined to the override
    /// package and never allowed into Workshop or base-game folders.
    /// </summary>
    public static SafetyVerdict CanWriteOverrideFile(string filePath, ElinPaths paths)
    {
        var target = Normalize(filePath);
        if (target is null) return SafetyVerdict.Deny("Path is empty or malformed.");

        var packageRoot = Normalize(paths.OverridePackageRoot);
        if (packageRoot is null)
            return SafetyVerdict.Deny("Override package folder is not configured.");

        if (!IsInside(target, packageRoot))
            return SafetyVerdict.Deny("Write target is outside the override package.");

        if (!string.IsNullOrWhiteSpace(paths.WorkshopRoot) && IsInside(target, paths.WorkshopRoot))
            return SafetyVerdict.Deny("Refusing to write into the Steam Workshop folder.");

        if (IsInside(target, Path.Combine(paths.ElinRoot, "Elin_Data")))
            return SafetyVerdict.Deny("Refusing to write into the base game asset folder.");

        return SafetyVerdict.Ok;
    }

    /// <summary>
    /// Rejects file names that could escape the override folder. Selected textures are
    /// named from Workshop files, so the name is untrusted input.
    /// </summary>
    public static bool IsSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName is "." or "..") return false;

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (fileName.Contains('/') || fileName.Contains('\\')) return false;

        // Path.GetFileName round-trips only for a plain file name.
        return string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal);
    }

    /// <summary>Logs and returns the verdict, so denials always leave a trace.</summary>
    public static SafetyVerdict Audit(string operation, string path, SafetyVerdict verdict)
    {
        if (verdict.Allowed) AppLog.Info($"{operation}: {path}");
        else AppLog.Warn($"{operation} DENIED for '{path}': {verdict.Reason}");
        return verdict;
    }
}
