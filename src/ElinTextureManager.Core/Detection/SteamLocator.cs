using ElinTextureManager.Core.Logging;
using Microsoft.Win32;

namespace ElinTextureManager.Core.Detection;

public sealed record SteamLibrary(string Path)
{
    public string SteamAppsPath => System.IO.Path.Combine(Path, "steamapps");
    public string CommonPath => System.IO.Path.Combine(SteamAppsPath, "common");

    public string WorkshopContentPath(int appId) =>
        System.IO.Path.Combine(SteamAppsPath, "workshop", "content", appId.ToString());
}

/// <summary>
/// Finds Steam and its library folders. Nothing here is hardcoded to C:\ - the
/// registry gives us Steam's root and libraryfolders.vdf gives us every library,
/// including ones on other drives.
/// </summary>
public static class SteamLocator
{
    public const int ElinAppId = 2135150;

    public static string? FindSteamRoot()
    {
        foreach (var probe in EnumerateRegistryCandidates())
        {
            if (!string.IsNullOrWhiteSpace(probe) && Directory.Exists(probe))
            {
                try { return Path.GetFullPath(probe); }
                catch { /* malformed registry value */ }
            }
        }

        // Registry unavailable: fall back to conventional locations, still
        // enumerating every fixed drive rather than assuming C:.
        foreach (var drive in SafeDrives())
        {
            foreach (var rel in new[] { @"Program Files (x86)\Steam", @"Program Files\Steam", "Steam" })
            {
                var p = Path.Combine(drive, rel);
                if (Directory.Exists(Path.Combine(p, "steamapps"))) return p;
            }
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateRegistryCandidates()
    {
        yield return ReadRegistry(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        yield return ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        yield return ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
    }

    private static string? ReadRegistry(RegistryHive hive, string subKey, string value)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(value) as string;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Registry read failed for {subKey}: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }

        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.IsReady && d.DriveType == DriveType.Fixed; }
            catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    /// <summary>
    /// Returns Steam's own folder plus every library listed in libraryfolders.vdf.
    /// </summary>
    public static IReadOnlyList<SteamLibrary> FindLibraries(string? steamRoot = null)
    {
        var result = new List<SteamLibrary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        steamRoot ??= FindSteamRoot();
        if (steamRoot is null) return result;

        void Add(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (Directory.Exists(Path.Combine(full, "steamapps")) && seen.Add(full))
                    result.Add(new SteamLibrary(full));
            }
            catch { }
        }

        Add(steamRoot);

        foreach (var vdf in new[]
                 {
                     Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                     Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
                 })
        {
            foreach (var p in ParseLibraryFolders(vdf)) Add(p);
        }

        return result;
    }

    /// <summary>
    /// Extracts the "path" values from a libraryfolders.vdf. The file is Valve's
    /// KeyValues format; we only need one key, so a targeted scan beats a full parser.
    /// </summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfPath)
    {
        var paths = new List<string>();
        try
        {
            if (!File.Exists(vdfPath)) return paths;

            foreach (var raw in File.ReadLines(vdfPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] != '"') continue;

                var parts = SplitQuoted(line);
                if (parts.Count >= 2 && string.Equals(parts[0], "path", StringComparison.OrdinalIgnoreCase))
                    paths.Add(parts[1].Replace(@"\\", @"\"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not parse libraryfolders.vdf: {ex.Message}");
        }

        return paths;
    }

    /// <summary>Splits a KeyValues line into its quoted tokens.</summary>
    public static List<string> SplitQuoted(string line)
    {
        var tokens = new List<string>();
        var inQuote = false;
        var current = new System.Text.StringBuilder();

        foreach (var c in line)
        {
            if (c == '"')
            {
                if (inQuote) { tokens.Add(current.ToString()); current.Clear(); }
                inQuote = !inQuote;
            }
            else if (inQuote) current.Append(c);
        }

        return tokens;
    }
}
