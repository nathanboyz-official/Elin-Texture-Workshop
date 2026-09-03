using System.IO;
using ElinTextureManager.Core.Bisect;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.App.Services;

/// <summary>Outcome of a round being written to the load order.</summary>
public sealed record BisectStepResult(bool Success, int Changed, string? Error)
{
    public string Message => Error ?? $"{Changed} mods switched for this round.";
}

/// <summary>
/// Drives a <see cref="BisectSession"/> against the real load order.
///
/// The session decides which mods should be on; this turns that into writes, and - more
/// importantly - can always put the load order back exactly as it found it. Every round
/// is written to disk before the user is asked anything, so closing the application
/// halfway through leaves a recoverable state rather than 338 mods in an arrangement
/// nobody chose.
/// </summary>
public sealed class BisectRunner
{
    private readonly AppServices _app;

    private BisectSession? _session;
    private Dictionary<string, bool> _originalStates = new(StringComparer.OrdinalIgnoreCase);

    public BisectRunner(AppServices app) => _app = app;

    public BisectSession? Session => _session;

    public bool IsRunning => _session is { IsRunning: true };

    /// <summary>
    /// Mods other installed mods are built against.
    ///
    /// These are held on for the whole search. Turning a framework off does not test the
    /// framework - it breaks everything that needs it, and the resulting failure is a
    /// different one from the failure being chased.
    /// </summary>
    public IReadOnlyList<ModPackage> FindFrameworks()
    {
        var providers = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var codeMods = new List<(ModPackage Mod, List<string> Dlls)>();

        foreach (var mod in _app.Scan.Mods.Where(m => m.CanToggle))
        {
            List<string> dlls;
            try
            {
                dlls = Directory.GetFiles(mod.Directory, "*.dll", SearchOption.AllDirectories)
                    .Where(AssemblyIndex.IsManaged).ToList();
            }
            catch { continue; }

            if (dlls.Count == 0) continue;

            codeMods.Add((mod, dlls));
            foreach (var dll in dlls) providers.TryAdd(Path.GetFileNameWithoutExtension(dll), mod);
        }

        foreach (var (mod, dlls) in codeMods)
        foreach (var dll in dlls)
        foreach (var reference in AssemblyIndex.AssemblyRefs(dll))
        {
            if (AssemblyIndex.IsAmbientAssembly(reference)) continue;
            if (!providers.TryGetValue(reference, out var provider)) continue;
            if (provider.Key == mod.Key) continue;

            needed.Add(provider.Key);
        }

        return _app.Scan.Mods.Where(m => needed.Contains(m.Key)).ToList();
    }

    /// <summary>
    /// Begins a search, recording every mod's current state first so it can be undone.
    /// </summary>
    public BisectStepResult Start(IEnumerable<ModPackage> candidates,
        IEnumerable<ModPackage> pinned, string? problem)
    {
        _originalStates = _app.Scan.Mods
            .Where(m => m.CanToggle)
            .ToDictionary(m => m.Key, m => m.Enabled, StringComparer.OrdinalIgnoreCase);

        var pinnedKeys = pinned.Select(m => m.Key).ToList();
        var candidateKeys = candidates.Select(m => m.Key)
            .Where(k => !pinnedKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToList();

        _session = new BisectSession(candidateKeys, pinnedKeys, problem);
        _session.Begin();

        AppLog.Info($"Bisect started: {candidateKeys.Count} candidates, "
                    + $"{pinnedKeys.Count} held on. Problem: {problem}");

        return ApplyRound();
    }

    /// <summary>Records the answer to this round and sets up the next one.</summary>
    public BisectStepResult Answer(BisectAnswer answer)
    {
        if (_session is null || !_session.IsRunning)
            return new BisectStepResult(false, 0, "No search is running.");

        _session.Answer(answer);

        // Once it has an answer the load order goes back to how the user had it. Leaving
        // it on the last trial would mean the search silently changed their setup.
        return _session.IsRunning ? ApplyRound() : Finish();
    }

    /// <summary>Abandons the search and puts every mod back.</summary>
    public BisectStepResult Cancel()
    {
        _session?.Cancel();
        return Finish();
    }

    /// <summary>
    /// Writes the on/off states this round needs: pinned and trial mods on, the rest of
    /// the suspects off, and every mod outside the search left exactly as it was.
    /// </summary>
    private BisectStepResult ApplyRound()
    {
        if (_session is null) return new BisectStepResult(false, 0, "No search is running.");

        var wanted = new Dictionary<string, bool>(_originalStates, StringComparer.OrdinalIgnoreCase);

        foreach (var key in _session.Suspects) wanted[key] = false;
        foreach (var key in _session.Cleared) wanted[key] = false;
        foreach (var key in _session.TrialGroup) wanted[key] = true;
        foreach (var key in _session.PinnedOn) wanted[key] = true;

        var result = Write(wanted);

        // Saved before the user is asked anything, so the answer to "what was I in the
        // middle of" survives the application closing.
        BisectState.Save(AppPaths.BisectFile, BisectState.Capture(_session, _originalStates));

        return result;
    }

    /// <summary>Restores the original states and clears the saved session.</summary>
    private BisectStepResult Finish()
    {
        var result = Write(_originalStates);
        BisectState.Clear(AppPaths.BisectFile);

        AppLog.Info($"Bisect finished: {_session?.Outcome}. Load order restored.");
        return result;
    }

    private BisectStepResult Write(IReadOnlyDictionary<string, bool> wanted)
    {
        var changes = new List<(ModPackage Mod, bool Enabled)>();

        foreach (var mod in _app.Scan.Mods.Where(m => m.CanToggle))
        {
            if (!wanted.TryGetValue(mod.Key, out var enabled)) continue;
            if (mod.Enabled == enabled) continue;

            changes.Add((mod, enabled));
        }

        if (changes.Count == 0) return new BisectStepResult(true, 0, null);

        var result = _app.ApplyModEnabledStates(changes);
        return new BisectStepResult(result.Success, result.Changed, result.Error);
    }

    /// <summary>
    /// Puts the load order back after the application closed mid-search.
    ///
    /// This is the reason the session is written to disk at all. Without it, closing the
    /// window during a search leaves half the user's mods off with no record of which
    /// half, and at 338 mods that is not something anyone is going to fix by hand.
    /// Returns a message when something was actually recovered.
    /// </summary>
    public string? RecoverIfInterrupted()
    {
        var state = BisectState.Load(AppPaths.BisectFile);
        if (state is null) return null;

        if (!state.NeedsRestore)
        {
            BisectState.Clear(AppPaths.BisectFile);
            return null;
        }

        var result = Write(state.OriginalStates);
        BisectState.Clear(AppPaths.BisectFile);

        AppLog.Info($"Recovered an interrupted bisect from {state.StartedUtc:u}; "
                    + $"{result.Changed} mods put back.");

        return result.Success
            ? $"A mod search was interrupted, so your load order had {state.Suspects.Count} "
              + $"mods switched off that you did not switch off. It has been put back "
              + $"the way it was ({result.Changed} changed)."
            : $"A mod search was interrupted and the load order could not be put back: {result.Error}";
    }
}
