using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Detection;

/// <summary>
/// The set of Elin-related folders the application works with.
/// Every path is derived from a detected (or user-chosen) Elin install root.
/// </summary>
public sealed class ElinPaths
{
    /// <summary>Folder name of the package this application writes selected textures into.</summary>
    public const string OverridePackageName = "ElinTextureManager_Overrides";

    /// <summary>The directory segment Elin uses for replacement textures.</summary>
    public const string TextureReplaceFolder = "Texture Replace";

    public required string ElinRoot { get; init; }

    /// <summary>Workshop content folder for app 2135150. May be null if not found.</summary>
    public string? WorkshopRoot { get; set; }

    public string PackageRoot => Path.Combine(ElinRoot, "Package");
    public string LoadOrderFile => Path.Combine(ElinRoot, "loadorder.txt");
    public string ExecutablePath => Path.Combine(ElinRoot, "Elin.exe");

    /// <summary>Elin's own user-level texture replace folder (Elin\User\Texture Replace).</summary>
    public string UserTextureReplace => Path.Combine(ElinRoot, "User", TextureReplaceFolder);

    /// <summary>Our override package: Elin\Package\ElinTextureManager_Overrides.</summary>
    public string OverridePackageRoot => Path.Combine(PackageRoot, OverridePackageName);

    public string OverrideTextureRoot => Path.Combine(OverridePackageRoot, TextureReplaceFolder);

    public string OverridePackageXml => Path.Combine(OverridePackageRoot, "package.xml");

    /// <summary>The directory segment Elin uses for portrait replacements.</summary>
    public const string PortraitFolder = "Portrait";

    /// <summary>
    /// The base game's own package, Elin\Package\_Elona. Its loose images are the
    /// originals a replacement is measured against.
    /// </summary>
    public string VanillaPackageRoot => Path.Combine(PackageRoot, "_Elona");

    /// <summary>Our override package's Portrait folder.</summary>
    public string OverridePortraitRoot => Path.Combine(OverridePackageRoot, PortraitFolder);

    /// <summary>
    /// Which folder inside the override package a replacement of the given kind is
    /// written to. A package mirrors the layout of _Elona, so the kind picks the folder.
    /// </summary>
    public string OverrideRootFor(Model.ReplacementKind kind) => kind switch
    {
        Model.ReplacementKind.Portrait => OverridePortraitRoot,
        _ => OverrideTextureRoot,
    };

    public bool LooksValid => Directory.Exists(ElinRoot) && File.Exists(ExecutablePath);

    public bool HasLoadOrderFile => File.Exists(LoadOrderFile);

    /// <summary>
    /// A directory is an Elin install if it has Elin.exe. Elin_Data is checked as a
    /// secondary signal so a folder containing only a shortcut is not accepted.
    /// </summary>
    public static bool IsElinInstall(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                   && File.Exists(Path.Combine(directory, "Elin.exe"))
                   && Directory.Exists(Path.Combine(directory, "Elin_Data"));
        }
        catch { return false; }
    }

    /// <summary>
    /// Detects Elin and its Workshop folder across every Steam library.
    /// Returns null when nothing convincing is found - the caller then asks the user to browse.
    /// </summary>
    public static ElinPaths? Detect()
    {
        var libraries = SteamLocator.FindLibraries();
        AppLog.Info($"Steam libraries found: {libraries.Count}");

        string? elinRoot = null;
        string? workshop = null;

        foreach (var lib in libraries)
        {
            var candidate = Path.Combine(lib.CommonPath, "Elin");
            if (elinRoot is null && IsElinInstall(candidate))
            {
                elinRoot = candidate;
                AppLog.Info($"Elin install detected: {candidate}");
            }

            var ws = lib.WorkshopContentPath(SteamLocator.ElinAppId);
            if (workshop is null && Directory.Exists(ws))
            {
                workshop = ws;
                AppLog.Info($"Workshop content detected: {ws}");
            }
        }

        if (elinRoot is null) return null;

        var paths = new ElinPaths { ElinRoot = elinRoot, WorkshopRoot = workshop };
        paths.WorkshopRoot ??= FindWorkshopNear(elinRoot);
        return paths;
    }

    /// <summary>
    /// Given an Elin folder, walks up to the library root (…\steamapps\common\Elin)
    /// and looks for the sibling workshop content folder.
    /// </summary>
    public static string? FindWorkshopNear(string elinRoot)
    {
        try
        {
            // …\steamapps\common\Elin -> …\steamapps
            var common = Directory.GetParent(elinRoot);
            var steamApps = common?.Parent;
            if (steamApps is null) return null;

            var ws = Path.Combine(steamApps.FullName, "workshop", "content",
                SteamLocator.ElinAppId.ToString());
            return Directory.Exists(ws) ? ws : null;
        }
        catch { return null; }
    }

    /// <summary>Builds a path set from a user-chosen Elin folder.</summary>
    public static ElinPaths FromElinRoot(string elinRoot, string? workshopRoot = null)
    {
        var paths = new ElinPaths { ElinRoot = elinRoot, WorkshopRoot = workshopRoot };
        if (string.IsNullOrWhiteSpace(paths.WorkshopRoot) || !Directory.Exists(paths.WorkshopRoot))
            paths.WorkshopRoot = FindWorkshopNear(elinRoot);
        return paths;
    }
}

/// <summary>Local application folders: settings, cache, logs and backups.</summary>
public static class AppPaths
{
    /// <summary>
    /// Deliberately still "ElinTextureManager" even though the application is now called
    /// Elin Texture Workshop. This folder holds the user's texture choices, aliases and
    /// settings; renaming it would orphan all of them on the next launch for no gain.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElinTextureManager");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string SelectionsFile => Path.Combine(Root, "selections.json");
    public static string AliasFile => Path.Combine(Root, "aliases.json");
    public static string DatabaseFile => Path.Combine(Root, "cache.db");
    public static string NewsCacheFile => Path.Combine(Root, "news.json");
    public static string LogDirectory => Path.Combine(Root, "Logs");
    public static string BackupDirectory => Path.Combine(Root, "Backups");
    public static string ThumbnailCache => Path.Combine(Root, "Thumbnails");

    public static void EnsureCreated()
    {
        foreach (var d in new[] { Root, LogDirectory, BackupDirectory, ThumbnailCache })
        {
            try { Directory.CreateDirectory(d); }
            catch (Exception ex) { AppLog.Error($"Could not create {d}", ex); }
        }
    }
}
