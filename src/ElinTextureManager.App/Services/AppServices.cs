using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Scanning;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.App.Services;

/// <summary>
/// Shared application state: the detected paths, persisted settings and stores, the
/// current scan and the resolved winners. One instance lives for the lifetime of the app.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public SelectionStore Selections { get; private set; } = null!;
    public AliasStore Aliases { get; private set; } = null!;
    public CacheDatabase Cache { get; } = new();

    public ElinPaths? Paths { get; private set; }
    public OverrideManager? Overrides { get; private set; }

    public ScanResult Scan { get; private set; } = new();
    public LoadOrderDocument LoadOrder { get; set; } = new();
    public Dictionary<string, TextureWinner> Winners { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured => Paths is not null && Paths.LooksValid;

    /// <summary>Loads settings and stores, and works out where Elin lives.</summary>
    public void Initialize()
    {
        AppPaths.EnsureCreated();
        AppLog.Initialize(AppPaths.LogDirectory);

        Settings = AppSettings.Load(AppPaths.SettingsFile);

        Selections = new SelectionStore(AppPaths.SelectionsFile);
        Selections.Load();
        Selections.ActiveProfile = Settings.ActiveProfile;

        Aliases = new AliasStore(AppPaths.AliasFile);
        Aliases.Load();

        Cache.Open(AppPaths.DatabaseFile);

        ResolvePaths();
    }

    /// <summary>Uses the saved path when it is still valid, otherwise auto-detects.</summary>
    public void ResolvePaths()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ElinPath) && ElinPaths.IsElinInstall(Settings.ElinPath))
        {
            Paths = ElinPaths.FromElinRoot(Settings.ElinPath, Settings.WorkshopPath);
            AppLog.Info($"Using saved Elin path: {Paths.ElinRoot}");
        }
        else
        {
            Paths = ElinPaths.Detect();
            if (Paths is not null)
            {
                Settings.ElinPath = Paths.ElinRoot;
                Settings.WorkshopPath = Paths.WorkshopRoot;
                SaveSettings();
            }
        }

        if (Paths is not null) BuildOverrideManager();
    }

    /// <summary>Points the application at a folder the user chose.</summary>
    public bool SetElinPath(string elinRoot)
    {
        if (!ElinPaths.IsElinInstall(elinRoot))
        {
            AppLog.Warn($"Rejected Elin path (no Elin.exe / Elin_Data): {elinRoot}");
            return false;
        }

        Paths = ElinPaths.FromElinRoot(elinRoot);
        Settings.ElinPath = Paths.ElinRoot;
        Settings.WorkshopPath = Paths.WorkshopRoot;
        SaveSettings();
        BuildOverrideManager();
        return true;
    }

    public void SetWorkshopPath(string workshopRoot)
    {
        if (Paths is null) return;

        Paths.WorkshopRoot = workshopRoot;
        Settings.WorkshopPath = workshopRoot;
        SaveSettings();
    }

    private void BuildOverrideManager()
    {
        if (Paths is null) return;

        Overrides = new OverrideManager(Paths, Selections)
        {
            MirrorToUserFolder = Settings.MirrorOverridesToUserFolder,
        };
    }

    /// <summary>Runs a full scan and recomputes load order and winners.</summary>
    public async Task RefreshAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        if (Paths is null) return;

        var scanner = new ModScanner(new ScanOptions { ComputeHashes = true }, Cache);
        Scan = await scanner.ScanAsync(Paths, progress, ct).ConfigureAwait(false);

        LoadOrder = LoadOrderFile.Read(Paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(LoadOrder, Scan.Mods);

        Winners = new WinnerResolver(Settings.PriorityConvention).ResolveAll(Scan);
    }

    /// <summary>Recomputes winners after a selection changes, without a full rescan.</summary>
    public void RecomputeWinners()
    {
        Winners = new WinnerResolver(Settings.PriorityConvention).ResolveAll(Scan);
    }

    public void SaveSettings()
    {
        Settings.ActiveProfile = Selections?.ActiveProfile ?? "Default";
        Settings.Save(AppPaths.SettingsFile);
    }

    public void Dispose()
    {
        try { Selections?.Save(); } catch { }
        try { Aliases?.Save(); } catch { }
        try { Cache.Dispose(); } catch { }
    }
}
