using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Watching;

public enum WorkshopChangeKind { ModAdded, ModRemoved, ModUpdated }

public sealed record WorkshopChange(WorkshopChangeKind Kind, string ModFolder)
{
    public string ModId => Path.GetFileName(ModFolder.TrimEnd('\\', '/'));
}

/// <summary>
/// Watches the Workshop content folder for subscribe / unsubscribe / update activity.
///
/// Steam writes many files per mod update, so raw events are useless on their own.
/// Events are collapsed per mod folder and only reported once the folder has been quiet
/// for the debounce interval - that is what stops a mod update from triggering dozens
/// of rescans mid-download.
/// </summary>
public sealed class WorkshopWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _root;
    private readonly Dictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Timer _timer;
    private bool _disposed;

    /// <summary>How long a mod folder must be quiet before its change is reported.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Raised on a background thread once a mod folder settles.</summary>
    public event Action<WorkshopChange>? Changed;

    public WorkshopWatcher(string workshopRoot)
    {
        _root = Path.GetFullPath(workshopRoot);

        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size,
            // Steam can burst thousands of events; a large buffer avoids overflow.
            InternalBufferSize = 64 * 1024,
        };

        _watcher.Created += OnEvent;
        _watcher.Changed += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        _timer = new Timer(Flush, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            _watcher.EnableRaisingEvents = true;
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            AppLog.Info($"Watching workshop folder: {_root}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not start watching {_root}", ex);
        }
    }

    public void Stop()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch { }
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Touch(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Touch(e.FullPath);
        Touch(e.OldFullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow during a large Steam download: the watcher stays alive but we
        // may have missed events, so flag the whole root for a rescan.
        AppLog.Warn($"Workshop watcher error: {e.GetException().Message}");
        Touch(_root);
    }

    /// <summary>Records activity against the top-level mod folder the path belongs to.</summary>
    private void Touch(string fullPath)
    {
        var modFolder = ModFolderFor(fullPath);
        if (modFolder is null) return;

        lock (_gate) _pending[modFolder] = DateTime.UtcNow;
    }

    /// <summary>Maps any path under the root to its immediate mod folder (the Workshop ID).</summary>
    public string? ModFolderFor(string fullPath)
    {
        try
        {
            var full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return null;

            var relative = full[_root.Length..].TrimStart('\\', '/');
            if (relative.Length == 0) return null;

            var first = relative.Split('\\', '/')[0];
            return string.IsNullOrEmpty(first) ? null : Path.Combine(_root, first);
        }
        catch { return null; }
    }

    private void Flush(object? state)
    {
        List<string> ready;
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            ready = _pending
                .Where(kv => now - kv.Value >= Debounce)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in ready) _pending.Remove(key);
        }

        foreach (var folder in ready)
        {
            var kind = Directory.Exists(folder)
                ? WorkshopChangeKind.ModUpdated
                : WorkshopChangeKind.ModRemoved;

            AppLog.Info($"Workshop change settled: {kind} {Path.GetFileName(folder)}");

            try { Changed?.Invoke(new WorkshopChange(kind, folder)); }
            catch (Exception ex) { AppLog.Error("Workshop change handler failed", ex); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { Stop(); } catch { }
        try { _timer.Dispose(); } catch { }
        try { _watcher.Dispose(); } catch { }
    }
}
