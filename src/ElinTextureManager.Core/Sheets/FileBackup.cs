using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Sheets;

/// <summary>
/// Keeps a copy of a file before this application writes over it.
///
/// The files being edited belong to somebody's mod and often to somebody else's mod, and
/// a spreadsheet saved wrong is a day's work gone. Copies go into this application's own
/// folder rather than beside the original, so a mod folder never grows litter that the
/// game then tries to load.
/// </summary>
public static class FileBackup
{
    public static string Directory => Path.Combine(AppPaths.BackupDirectory, "Sheets");

    /// <summary>How many copies of one file to keep before the oldest is dropped.</summary>
    public const int Keep = 10;

    /// <summary>
    /// Copies the file. Returns where it went, or null when there was nothing to copy.
    /// Never throws: failing to keep a copy must not stop the save the user asked for,
    /// but it is logged, and the caller can say so.
    /// </summary>
    public static string? Take(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            System.IO.Directory.CreateDirectory(Directory);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = $"{Path.GetFileNameWithoutExtension(path)}.{stamp}{Path.GetExtension(path)}";
            var target = Path.Combine(Directory, name);

            File.Copy(path, target, overwrite: true);
            Prune(Path.GetFileNameWithoutExtension(path), Path.GetExtension(path));

            AppLog.Info($"Backed up {path} to {target}");
            return target;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not back up {path}", ex);
            return null;
        }
    }

    /// <summary>Every copy kept of a given file, newest first.</summary>
    public static IReadOnlyList<FileInfo> For(string path)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return Array.Empty<FileInfo>();

            var stem = Path.GetFileNameWithoutExtension(path);

            return new DirectoryInfo(Directory)
                .GetFiles($"{stem}.*{Path.GetExtension(path)}")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static void Prune(string stem, string extension)
    {
        try
        {
            var older = new DirectoryInfo(Directory)
                .GetFiles($"{stem}.*{extension}")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Keep)
                .ToList();

            foreach (var file in older) file.Delete();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not prune backups for {stem}: {ex.Message}");
        }
    }
}
