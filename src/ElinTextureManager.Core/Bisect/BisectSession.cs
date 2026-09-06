using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Bisect;

/// <summary>What the user saw the last time they launched the game.</summary>
public enum BisectAnswer
{
    /// <summary>The problem is still there, so the cause is among the mods left on.</summary>
    StillBroken = 0,

    /// <summary>The problem is gone, so the cause is among the mods that were turned off.</summary>
    Fixed = 1,
}

/// <summary>How a search ended.</summary>
public enum BisectOutcome
{
    Running = 0,

    /// <summary>Narrowed to a single mod.</summary>
    Found = 1,

    /// <summary>
    /// The problem happened with every candidate off, so it is not one of them - the
    /// game itself, a mod that was pinned on, or something that is not a mod at all.
    /// </summary>
    NotAMod = 2,

    /// <summary>
    /// The problem stopped happening even with everything on, so there is nothing left
    /// to search for. Usually an intermittent bug rather than a mod.
    /// </summary>
    NotReproducible = 3,

    Cancelled = 4,
}

/// <summary>
/// Narrows a broken game down to the mod that broke it, by halving.
///
/// Elin's own patch notes give this advice after every stable update - "mods from
/// previous versions may stop working; try disabling mods and testing again" - and
/// leave the player to do it by hand. Done one mod at a time that is 300-odd launches.
/// Halving turns it into nine.
///
/// The session is deliberately a plain state machine over mod keys with no file access
/// of its own. Whoever owns it writes the load order and launches the game, and this
/// only ever answers "turn these on, leave those off, then tell me what happened".
/// </summary>
public sealed class BisectSession
{
    private List<string> _suspects;

    public BisectSession(IEnumerable<string> suspects, IEnumerable<string> pinnedOn,
        string? problem = null)
    {
        _suspects = suspects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PinnedOn = pinnedOn.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Problem = problem;
        StartedUtc = DateTime.UtcNow;
    }

    /// <summary>What the user is chasing, in their own words. Only ever shown back to them.</summary>
    public string? Problem { get; set; }

    public DateTime StartedUtc { get; set; }

    /// <summary>
    /// Mods held on for the whole search: the ones other mods are built against, plus
    /// anything the user pinned. Turning a framework off does not test the framework, it
    /// breaks every mod that needs it and produces a different failure.
    /// </summary>
    public List<string> PinnedOn { get; }

    /// <summary>Still under suspicion. The search ends when one is left.</summary>
    public IReadOnlyList<string> Suspects => _suspects;

    /// <summary>Mods proven innocent, kept so the UI can show progress.</summary>
    public List<string> Cleared { get; } = new();

    public int Round { get; private set; }

    public BisectOutcome Outcome { get; private set; } = BisectOutcome.Running;

    /// <summary>The mod that did it, once the search has narrowed that far.</summary>
    public string? Culprit => Outcome == BisectOutcome.Found ? _suspects.FirstOrDefault() : null;

    public bool IsRunning => Outcome == BisectOutcome.Running;

    /// <summary>
    /// Rounds still to come, at worst. log2 of what is left - the number that makes this
    /// worth doing at all.
    /// </summary>
    public int RoundsRemaining => _suspects.Count <= 1
        ? (IsVerifying ? 1 : 0)
        : (int)Math.Ceiling(Math.Log2(_suspects.Count)) + 1;

    /// <summary>
    /// The half being tested this round: turned ON alongside everything pinned, with the
    /// rest of the suspects turned off.
    ///
    /// In the final round this is the last mod standing on its own, which is what turns
    /// "it was in this group" into "it is this mod".
    /// </summary>
    public IReadOnlyList<string> TrialGroup => IsControl
        ? Array.Empty<string>()
        : IsVerifying
            ? _suspects.Take(1).ToList()
            : _suspects.Take(_suspects.Count / 2).ToList();

    /// <summary>
    /// True on the first round, where every candidate is off and only the pinned mods
    /// run.
    ///
    /// This is the round that stops the search naming an innocent mod. Halving assumes
    /// one of the candidates is responsible; if the cause is really the game, a pinned
    /// framework, or a corrupt save, every round answers "still broken" and the search
    /// converges on whichever mod happened to be last - and the verify round confirms it,
    /// because the problem happens no matter what. One launch up front rules that out,
    /// and when it does fire it saves the other nine.
    /// </summary>
    public bool IsControl { get; private set; }

    /// <summary>The suspects turned off this round.</summary>
    public IReadOnlyList<string> RestingGroup =>
        _suspects.Except(TrialGroup, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// True on the last round, where the single remaining suspect runs by itself.
    ///
    /// Narrowing alone does not prove guilt. Halving can arrive at a mod by elimination
    /// without that mod ever having been the only one on, and naming it then would be
    /// the same mistake Elin's own crash dialog makes - confidently pointing at
    /// something that was merely present.
    /// </summary>
    public bool IsVerifying { get; private set; }

    /// <summary>Starts the search, or finishes it immediately if there is nothing to halve.</summary>
    public void Begin()
    {
        Round = 1;

        if (_suspects.Count == 0)
        {
            Outcome = BisectOutcome.NotAMod;
            return;
        }

        IsControl = true;
    }

    /// <summary>
    /// Records what the user saw and sets up the next round.
    ///
    /// The rule is the whole algorithm: if the problem is still there, the cause is
    /// among the mods that were on; if it is gone, the cause is among the mods that
    /// were off.
    /// </summary>
    public void Answer(BisectAnswer answer)
    {
        if (!IsRunning) return;

        if (IsControl)
        {
            IsControl = false;

            // Still broken with every candidate off: none of them is doing it.
            if (answer == BisectAnswer.StillBroken)
            {
                Outcome = BisectOutcome.NotAMod;
                return;
            }

            Round++;
            if (_suspects.Count == 1) IsVerifying = true;
            return;
        }

        if (IsVerifying)
        {
            Outcome = answer == BisectAnswer.StillBroken
                ? BisectOutcome.Found
                : BisectOutcome.NotReproducible;
            return;
        }

        var trial = TrialGroup;
        var resting = RestingGroup;

        if (answer == BisectAnswer.StillBroken)
        {
            Cleared.AddRange(resting);
            _suspects = trial.ToList();
        }
        else
        {
            Cleared.AddRange(trial);
            _suspects = resting.ToList();
        }

        Round++;

        if (_suspects.Count == 0)
        {
            // Both halves cleared. Nothing among the candidates does it on its own.
            Outcome = BisectOutcome.NotAMod;
            return;
        }

        if (_suspects.Count > 1) return;

        // Down to one. If it has already been seen running alone, that is proof enough;
        // otherwise it gets a round to itself.
        if (answer == BisectAnswer.StillBroken && trial.Count == 1) Outcome = BisectOutcome.Found;
        else IsVerifying = true;
    }

    public void Cancel() => Outcome = BisectOutcome.Cancelled;

    /// <summary>
    /// Rebuilds a session from what was written to disk, so reopening the application
    /// mid-search picks up at the round it was on rather than starting again.
    /// </summary>
    public static BisectSession Restore(IEnumerable<string> suspects, IEnumerable<string> pinnedOn,
        IEnumerable<string> cleared, string? problem, DateTime startedUtc,
        int round, bool isControl, bool isVerifying, BisectOutcome outcome)
    {
        var session = new BisectSession(suspects, pinnedOn, problem)
        {
            StartedUtc = startedUtc,
            Round = round,
            IsControl = isControl,
            IsVerifying = isVerifying,
            Outcome = outcome,
        };

        session.Cleared.AddRange(cleared);
        return session;
    }
}
