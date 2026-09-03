using System.Text.Json;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Bisect;

/// <summary>
/// A search, written to disk after every round.
///
/// The search works by turning half the user's mods off. If the application closes -
/// or crashes, or the machine loses power - between one round and the next, the load
/// order on disk is a half-disabled state the user never chose and has no way to undo
/// by hand at 338 mods. So the original on/off state of every mod is recorded before
/// anything is touched, and it is recorded on disk rather than in memory, because
/// memory is exactly what a crash takes with it.
/// </summary>
public sealed class BisectState
{
    public int FormatVersion { get; set; } = 1;

    public string? Problem { get; set; }
    public DateTime StartedUtc { get; set; }

    /// <summary>Every mod's on/off state as it was before the search began.</summary>
    public Dictionary<string, bool> OriginalStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Suspects { get; set; } = new();
    public List<string> Cleared { get; set; } = new();
    public List<string> PinnedOn { get; set; } = new();

    public int Round { get; set; }
    public bool IsControl { get; set; }
    public bool IsVerifying { get; set; }
    public BisectOutcome Outcome { get; set; } = BisectOutcome.Running;

    /// <summary>True while the load order on disk is something the search chose.</summary>
    public bool NeedsRestore => Outcome == BisectOutcome.Running;

    public static BisectState Capture(BisectSession session, IDictionary<string, bool> originalStates)
    {
        var state = new BisectState
        {
            Problem = session.Problem,
            StartedUtc = session.StartedUtc,
            Round = session.Round,
            IsControl = session.IsControl,
            IsVerifying = session.IsVerifying,
            Outcome = session.Outcome,
        };

        foreach (var (key, enabled) in originalStates) state.OriginalStates[key] = enabled;

        state.Suspects.AddRange(session.Suspects);
        state.Cleared.AddRange(session.Cleared);
        state.PinnedOn.AddRange(session.PinnedOn);

        return state;
    }

    /// <summary>Rebuilds a session mid-search, so a resumed search does not start over.</summary>
    public BisectSession ToSession() =>
        BisectSession.Restore(Suspects, PinnedOn, Cleared, Problem, StartedUtc,
            Round, IsControl, IsVerifying, Outcome);

    public static void Save(string path, BisectState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not save the bisect session to {path}", ex);
        }
    }

    public static BisectState? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var state = JsonSerializer.Deserialize<BisectState>(File.ReadAllText(path), Options);

            // A session with nothing recorded cannot restore anything, and acting on it
            // would be worse than ignoring it.
            return state is { OriginalStates.Count: > 0 } ? state : null;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read the bisect session at {path}", ex);
            return null;
        }
    }

    public static void Clear(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Error($"Could not clear the bisect session at {path}", ex); }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
}
