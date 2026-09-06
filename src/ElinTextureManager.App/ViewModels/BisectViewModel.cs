using System.Collections.ObjectModel;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Bisect;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>A mod held on for the whole search, shown so the user can see what is excluded.</summary>
public sealed class PinnedModViewModel
{
    public PinnedModViewModel(ModPackage mod) => Mod = mod;

    public ModPackage Mod { get; }
    public string Name => Mod.Name;
}

/// <summary>
/// The Find the Culprit page: halves the mod list until one mod is left.
///
/// Elin's release notes give this exact advice after every stable update - "mods from
/// previous versions may stop working; try disabling mods and testing again" - and then
/// leave the player to carry it out across however many mods they have. One at a time
/// that is three hundred launches. Halving makes it nine.
/// </summary>
public sealed class BisectViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onChanged;

    private string _problem = string.Empty;
    private string? _statusMessage;

    public BisectViewModel(AppServices app, Action onChanged)
    {
        _app = app;
        _onChanged = onChanged;

        StartCommand = new RelayCommand(_ => Start(), _ => CanStart);
        StillBrokenCommand = new RelayCommand(_ => Answer(BisectAnswer.StillBroken), _ => IsRunning);
        FixedCommand = new RelayCommand(_ => Answer(BisectAnswer.Fixed), _ => IsRunning);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
        LaunchGameCommand = new RelayCommand(_ => ShellService.LaunchElin());
    }

    public ObservableCollection<PinnedModViewModel> Pinned { get; } = new();

    public RelayCommand StartCommand { get; }
    public RelayCommand StillBrokenCommand { get; }
    public RelayCommand FixedCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand LaunchGameCommand { get; }

    private BisectSession? Session => _app.Bisect.Session;

    public bool IsRunning => _app.Bisect.IsRunning;

    public bool IsIdle => !IsRunning && !HasResult;

    public string Problem
    {
        get => _problem;
        set { SetProperty(ref _problem, value); OnPropertyChanged(nameof(CanStart)); }
    }

    public bool CanStart => !IsRunning && CandidateCount > 0;

    public int CandidateCount { get; private set; }

    public int PinnedCount => Pinned.Count;

    public string ScopeText => CandidateCount == 0
        ? "Nothing to search - no Workshop mods are switched on."
        : $"{CandidateCount} mods to search through, about {WorstCaseRounds} launches.";

    private int WorstCaseRounds => CandidateCount <= 1
        ? 2
        : (int)Math.Ceiling(Math.Log2(CandidateCount)) + 2;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    // ---- the round in progress ----

    public bool HasResult => Session is { IsRunning: false, Outcome: not BisectOutcome.Cancelled };

    public int Round => Session?.Round ?? 0;

    public int RoundsLeft => Session?.RoundsRemaining ?? 0;

    public bool IsControlRound => Session is { IsControl: true };

    public bool IsVerifyRound => Session is { IsVerifying: true };

    /// <summary>What to do before answering, written for someone who is not a programmer.</summary>
    public string RoundInstruction => Session switch
    {
        null => string.Empty,
        { IsControl: true } =>
            "Every mod being searched is now switched off. Start Elin and try to make the "
            + "problem happen. This round is here to prove a mod is causing it at all - if "
            + "it still happens with all of them off, no amount of searching will find a "
            + "mod to blame.",
        { IsVerifying: true } =>
            "Only the last suspect is switched on now. Start Elin and try to make the "
            + "problem happen. This is the round that turns a strong suspicion into an "
            + "answer - narrowing down to a mod is not the same as watching it do it.",
        _ =>
            "Half the remaining mods are switched off. Start Elin and try to make the "
            + "problem happen, then say what you saw.",
    };

    public string RoundTitle => Session switch
    {
        null => string.Empty,
        { IsControl: true } => "Round 1 - with everything off",
        { IsVerifying: true } => $"Round {Round} - the last suspect, on its own",
        _ => $"Round {Round} - {Session.Suspects.Count} mods still suspected",
    };

    public string TrialSummary => Session is null
        ? string.Empty
        : $"{Session.TrialGroup.Count} on  ·  {Session.RestingGroup.Count + Session.Cleared.Count} off  "
          + $"·  {Session.PinnedOn.Count} held on";

    /// <summary>True while Elin is running, when writing the load order achieves nothing.</summary>
    public bool GameIsRunning => GameProcessDetector.IsElinRunning();

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(Round));
        OnPropertyChanged(nameof(RoundsLeft));
        OnPropertyChanged(nameof(IsControlRound));
        OnPropertyChanged(nameof(IsVerifyRound));
        OnPropertyChanged(nameof(RoundInstruction));
        OnPropertyChanged(nameof(RoundTitle));
        OnPropertyChanged(nameof(TrialSummary));
        OnPropertyChanged(nameof(GameIsRunning));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(CulpritName));
        OnPropertyChanged(nameof(HasCulprit));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(CandidateCount));
        OnPropertyChanged(nameof(PinnedCount));
    }

    // ---- the answer ----

    public string? CulpritName
    {
        get
        {
            var key = Session?.Culprit;
            return key is null ? null : _app.Scan.Mods.FirstOrDefault(m => m.Key == key)?.Name ?? key;
        }
    }

    public bool HasCulprit => CulpritName is not null;

    public string ResultTitle => Session?.Outcome switch
    {
        BisectOutcome.Found => $"It is {CulpritName}",
        BisectOutcome.NotAMod => "None of the searched mods is doing this",
        BisectOutcome.NotReproducible => "The problem stopped happening",
        _ => string.Empty,
    };

    public string ResultDetail => Session?.Outcome switch
    {
        BisectOutcome.Found =>
            "That mod was the only one switched on when the problem happened, and the "
            + "problem went away without it. Your load order has been put back the way it "
            + "was, so this mod is still on - turn it off on the Mods page when you are ready.",
        BisectOutcome.NotAMod =>
            "It still happened with every searched mod switched off, so the cause is "
            + "somewhere else: the game itself, one of the mods held on, or the save. "
            + "Mod Health is the next place to look.",
        BisectOutcome.NotReproducible =>
            "The last suspect could not make it happen on its own. That usually means two "
            + "mods only misbehave together, or the problem is intermittent. Nothing has "
            + "been blamed, and your load order is back the way it was.",
        _ => string.Empty,
    };

    // ---- actions ----

    /// <summary>
    /// Fills in the candidates: every Workshop mod currently on, minus the ones other
    /// mods are built against.
    /// </summary>
    public void Apply()
    {
        Pinned.Clear();

        var frameworks = _app.Bisect.FindFrameworks();
        foreach (var mod in frameworks.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
            Pinned.Add(new PinnedModViewModel(mod));

        var pinnedKeys = frameworks.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CandidateCount = _app.Scan.Mods.Count(m => m.CanToggle && m.Enabled && !pinnedKeys.Contains(m.Key));

        Refresh();
    }

    private IEnumerable<ModPackage> Candidates(IReadOnlyCollection<ModPackage> pinned)
    {
        var pinnedKeys = pinned.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _app.Scan.Mods.Where(m => m.CanToggle && m.Enabled && !pinnedKeys.Contains(m.Key));
    }

    private void Start()
    {
        var pinned = Pinned.Select(p => p.Mod).ToList();
        var result = _app.Bisect.Start(Candidates(pinned), pinned,
            string.IsNullOrWhiteSpace(Problem) ? null : Problem.Trim());

        StatusMessage = result.Success
            ? "Round 1 is set up. Start Elin and see what happens."
            : result.Message;

        Refresh();
        _onChanged();
    }

    private void Answer(BisectAnswer answer)
    {
        var result = _app.Bisect.Answer(answer);
        StatusMessage = result.Success ? null : result.Message;

        Refresh();
        _onChanged();
    }

    private void Cancel()
    {
        var result = _app.Bisect.Cancel();
        StatusMessage = result.Success
            ? "Search stopped. Your load order has been put back the way it was."
            : result.Message;

        Refresh();
        _onChanged();
    }
}
