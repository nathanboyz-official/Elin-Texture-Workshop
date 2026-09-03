using System.IO;
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

    /// <summary>The base game's own loose images, used to show a texture's original.</summary>
    public VanillaAssets Vanilla { get; private set; } = new();

    /// <summary>Colour index behind the reverse lookup. Built on demand, cached on disk.</summary>
    public SignatureIndex Signatures { get; }

    /// <summary>What Steam publishes about the installed Workshop items. Opt-in.</summary>
    public WorkshopStatusService Workshop { get; }

    /// <summary>Drives the halving search that finds which mod broke the game.</summary>
    public BisectRunner Bisect { get; }

    public AppServices()
    {
        Signatures = new SignatureIndex(Cache);
        Bisect = new BisectRunner(this);
        Workshop = new WorkshopStatusService(this);
    }

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
        Workshop.LoadCache();

        ResolvePaths();
    }

    /// <summary>Uses the saved path when it is still valid, otherwise auto-detects.</summary>
    public void ResolvePaths()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ElinPath) && ElinPaths.IsElinInstall(Settings.ElinPath))
        {
            Paths = ElinPaths.FromElinRoot(Settings.ElinPath, Settings.WorkshopPath);
            AppLog.Info($"Using saved Elin path: {Paths.ElinRoot}");

            // A path remembered from an older build can carry the wrong letter case.
            // Every mod directory is derived from these and ends up in loadorder.txt,
            // where the game matches on the string, so write the resolved spelling back
            // rather than re-deriving it correctly but remembering it wrongly.
            if (!string.Equals(Settings.ElinPath, Paths.ElinRoot, StringComparison.Ordinal)
                || !string.Equals(Settings.WorkshopPath, Paths.WorkshopRoot, StringComparison.Ordinal))
            {
                AppLog.Info($"Corrected stored paths to their on-disk spelling: {Paths.ElinRoot}");
                Settings.ElinPath = Paths.ElinRoot;
                Settings.WorkshopPath = Paths.WorkshopRoot;
                SaveSettings();
            }
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

        Vanilla = VanillaAssets.Load(Paths);
        Vanilla.AttachTo(Scan);

        Winners = new WinnerResolver(Settings.PriorityConvention).ResolveAll(Scan);
    }

    /// <summary>
    /// Writes a set of enable/disable changes to loadorder.txt in one pass. Elin's file is
    /// the only place a whole mod can be switched off, and it is backed up before every
    /// write - a failed backup refuses the save outright.
    /// </summary>
    public ModToggleResult ApplyModEnabledStates(IEnumerable<(ModPackage Mod, bool Enabled)> changes)
    {
        if (Paths is null) return new ModToggleResult(false, 0, null, "Elin folder is not set.");

        var changed = 0;
        foreach (var (mod, enabled) in changes)
            if (LoadOrderFile.SetEnabled(LoadOrder, mod.Directory, enabled)) changed++;

        if (changed == 0)
            return new ModToggleResult(true, 0, null, "Nothing to change.");

        if (!LoadOrderFile.Save(LoadOrder, AppPaths.BackupDirectory, out var backup))
            return new ModToggleResult(false, 0,  null,
                "Could not write loadorder.txt - the original file was left untouched. See the log.");

        LoadOrderFile.ApplyTo(LoadOrder, Scan.Mods);
        RecomputeWinners();

        return new ModToggleResult(true, changed, backup, null);
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

    /// <summary>
    /// Makes the override package on disk match a profile's selections.
    ///
    /// The package is cleared first rather than reconciled. Reconciling would be faster
    /// and would also have to be right about every case - a texture in both profiles
    /// from different mods, a texture in neither, a source mod Steam has updated since -
    /// and being wrong leaves the game loading an image the profile does not name.
    /// Clearing costs a second and cannot be subtly wrong.
    /// </summary>
    public ProfileApplyResult ApplyActiveProfile()
    {
        if (Overrides is null) return new ProfileApplyResult(false, 0, 0, "Elin folder is not set.");

        var wanted = Selections.All().ToList();

        Overrides.ClearAll();

        var applied = 0;
        var unavailable = 0;

        foreach (var selection in wanted)
        {
            var source = FindSelectionSource(selection);
            if (source is null) { unavailable++; continue; }

            if (Overrides.Select(source).Success) applied++;
            else unavailable++;
        }

        Selections.Save();
        RecomputeWinners();

        return new ProfileApplyResult(true, applied, unavailable, null);
    }

    /// <summary>
    /// Finds the file a selection points at in the current library, by mod rather than
    /// by path: Steam moves and rewrites files, and the mod that won is the choice.
    /// </summary>
    private TextureFile? FindSelectionSource(OverrideSelection selection)
    {
        if (!Scan.Index.TryGetValue(selection.TextureId, out var entry)) return null;

        return entry.Versions.FirstOrDefault(v =>
                   v.ModKey == selection.SourceModKey)
               ?? entry.Versions.FirstOrDefault(v =>
                   selection.SourceWorkshopId is not null
                   && v.WorkshopId == selection.SourceWorkshopId);
    }

    /// <summary>Switches profile and rewrites the override package to match.</summary>
    public ProfileApplyResult SwitchProfile(string profile)
    {
        Selections.ActiveProfile = profile;
        Settings.ActiveProfile = profile;
        SaveSettings();

        return ApplyActiveProfile();
    }

    public void Dispose()
    {
        try { Selections?.Save(); } catch { }
        try { Aliases?.Save(); } catch { }
        try { Cache.Dispose(); } catch { }
    }
}

/// <summary>Outcome of making the override package match a profile.</summary>
public sealed record ProfileApplyResult(bool Success, int Applied, int Unavailable, string? Error)
{
    public string Message => Error
        ?? (Unavailable == 0
            ? $"{Applied} textures applied."
            : $"{Applied} textures applied, {Unavailable} skipped - the mod they came from is not installed any more.");
}

/// <summary>Outcome of writing mod enable/disable changes to loadorder.txt.</summary>
public sealed record ModToggleResult(bool Success, int Changed, string? BackupPath, string? Error)
{
    public string Message => Error
        ?? (Changed == 0
            ? "Nothing to change."
            : BackupPath is null
                ? $"{Changed} mod{(Changed == 1 ? "" : "s")} updated."
                : $"{Changed} mod{(Changed == 1 ? "" : "s")} updated. "
                  + $"Backup: {Path.GetFileName(BackupPath)}");
}
