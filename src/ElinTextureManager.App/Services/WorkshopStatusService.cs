using System.IO;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Workshop;

namespace ElinTextureManager.App.Services;

/// <summary>
/// Keeps what Steam publishes about the installed Workshop items.
///
/// Nothing here runs unless the user has turned it on in Settings. The cache is loaded
/// at startup regardless, because a cached answer is local data and showing it costs
/// nothing - the switch governs going to the network, not remembering what came back.
/// </summary>
public sealed class WorkshopStatusService
{
    private readonly AppServices _app;
    private readonly WorkshopClient _client = new();

    private Dictionary<string, WorkshopItem> _items = new(StringComparer.Ordinal);

    public WorkshopStatusService(AppServices app) => _app = app;

    public IReadOnlyDictionary<string, WorkshopItem> Items => _items;

    public DateTime? FetchedUtc { get; private set; }

    public bool HasData => _items.Count > 0;

    /// <summary>Reads the last answer off disk. No network, so it is safe to call always.</summary>
    public void LoadCache()
    {
        var cached = WorkshopCache.Load(AppPaths.WorkshopCacheFile);
        if (cached is not { } hit) return;

        _items = hit.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        FetchedUtc = hit.FetchedUtc;

        AppLog.Info($"Workshop cache: {_items.Count} items from {hit.FetchedUtc:u}.");
    }

    /// <summary>
    /// Asks Steam about every installed Workshop item. Refuses unless the setting is on,
    /// so there is exactly one place this can start from.
    /// </summary>
    public async Task<string?> RefreshAsync(IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!_app.Settings.EnableWorkshopChecks)
            return "Workshop checks are turned off in Settings.";

        var ids = _app.Scan.Mods
            .Where(m => m.SourceType == TextureSourceType.Workshop && m.WorkshopId is not null)
            .Select(m => m.WorkshopId!)
            .ToList();

        // Also the load-order lines pointing at folders that are gone. Those are the
        // ones a name would help most with - the entry is only a path, the mod is not
        // installed to read a title from, and Steam is the only thing that still knows
        // what it was called or whether it is still there to re-subscribe to.
        foreach (var entry in _app.LoadOrder.Entries.Where(e => e.IsParsed))
        {
            var id = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar));
            if (id.Length > 0 && id.All(char.IsDigit)) ids.Add(id);
        }

        if (ids.Count == 0) return "There are no Workshop mods installed.";

        var result = await _client.FetchAsync(ids, progress, ct).ConfigureAwait(false);

        if (result.Items.Count > 0)
        {
            _items = result.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
            FetchedUtc = DateTime.UtcNow;
            WorkshopCache.Save(AppPaths.WorkshopCacheFile, result.Items);
        }

        return result.Success
            ? null
            : $"Could not reach Steam for all of them: {result.Error}";
    }

    public WorkshopState StateOf(ModPackage mod)
    {
        if (mod.WorkshopId is null) return WorkshopState.Unknown;
        return WorkshopStatus.For(mod, _items.GetValueOrDefault(mod.WorkshopId));
    }

    /// <summary>Mods with a newer version published than the copy on disk.</summary>
    public int UpdateCount => _app.Scan.Mods.Count(m => StateOf(m) == WorkshopState.UpdateWaiting);
}
