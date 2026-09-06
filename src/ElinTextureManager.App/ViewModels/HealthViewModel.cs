using System.Collections.ObjectModel;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Health;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One mod named by a finding, with the button to switch it off.</summary>
public sealed class HealthModViewModel : ObservableObject
{
    public HealthModViewModel(string name, ModPackage? mod, bool offerDisable)
    {
        Name = name;
        Mod = mod;
        OfferDisable = offerDisable;
    }

    public string Name { get; }
    public ModPackage? Mod { get; }

    /// <summary>
    /// Whether turning this mod off is the remedy the finding actually recommends.
    /// A notice that says "this is normal" must not put a row of switches under it.
    /// </summary>
    public bool OfferDisable { get; }

    /// <summary>False for mods already off, and for anything not from the Workshop.</summary>
    public bool CanDisable => OfferDisable && Mod is { CanToggle: true, Enabled: true };

    public bool IsAlreadyOff => Mod is { Enabled: false };

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanDisable));
        OnPropertyChanged(nameof(IsAlreadyOff));
    }
}

/// <summary>One finding, as a card.</summary>
public sealed class HealthFindingViewModel : ObservableObject
{
    private bool _expanded;

    public HealthFindingViewModel(HealthViewModel owner, HealthFinding finding)
    {
        Finding = finding;

        // Only a Broken finding recommends switching something off. On a notice the
        // mods are named so you know who is involved, not so you can start cutting.
        var offerDisable = finding.Severity == HealthSeverity.Broken;

        for (var i = 0; i < finding.ModNames.Count; i++)
        {
            var key = i < finding.ModKeys.Count ? finding.ModKeys[i] : null;
            Mods.Add(new HealthModViewModel(finding.ModNames[i], owner.FindMod(key), offerDisable));
        }
    }

    public HealthFinding Finding { get; }

    public ObservableCollection<HealthModViewModel> Mods { get; } = new();

    public string Title => Finding.Title;
    public string Detail => Finding.Detail;
    public string Check => Finding.Check;
    public string SeverityLabel => Finding.SeverityLabel;
    public string? Suggestion => Finding.Suggestion;

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Finding.Suggestion);
    public bool HasEvidence => Finding.Evidence.Count > 0;
    public bool HasMods => Mods.Count > 0;

    public IReadOnlyList<string> Evidence => Finding.Evidence;

    public bool IsBroken => Finding.Severity == HealthSeverity.Broken;
    public bool IsConflict => Finding.Severity == HealthSeverity.Conflict;
    public bool IsNotice => Finding.Severity == HealthSeverity.Notice;

    /// <summary>Evidence starts folded: the headline is the point, signatures are the proof.</summary>
    public bool Expanded
    {
        get => _expanded;
        set { SetProperty(ref _expanded, value); OnPropertyChanged(nameof(ExpandLabel)); }
    }

    public string ExpandLabel => Expanded ? "Hide details" : "Show details";

    public void RefreshMods()
    {
        foreach (var m in Mods) m.Refresh();
    }
}

/// <summary>
/// The Health page: what is actually wrong with the installed set of mods.
///
/// This exists because Elin's own crash dialog is misleading. It lists the Harmony
/// patches wrapping a failed call, so the mod at the top of the trace is usually
/// innocent and the one people uninstall. These checks name the mod that made the
/// bad call instead.
/// </summary>
public sealed class HealthViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly HealthScanner _scanner = new();
    private readonly Action? _onChanged;

    private HealthReport? _report;
    private string? _statusMessage;
    private bool _isRunning;
    private bool _hasRun;

    public HealthViewModel(AppServices app, Action? onChanged = null)
    {
        _app = app;
        _onChanged = onChanged;

        RunCommand = new AsyncRelayCommand(() => RunAsync(force: true), () => !IsRunning);
        ToggleExpandCommand = new RelayCommand(p =>
        {
            if (p is HealthFindingViewModel f) f.Expanded = !f.Expanded;
        });
        DisableModCommand = new RelayCommand(DisableMod,
            p => (p as HealthModViewModel)?.CanDisable == true);
    }

    public ObservableCollection<HealthFindingViewModel> Findings { get; } = new();

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand ToggleExpandCommand { get; }
    public RelayCommand DisableModCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetProperty(ref _isRunning, value);
            OnPropertyChanged(nameof(IsIdle));
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !_isRunning;

    public int BrokenCount => _report?.BrokenCount ?? 0;
    public int ConflictCount => _report?.ConflictCount ?? 0;
    public int NoticeCount => _report?.NoticeCount ?? 0;

    /// <summary>Only meaningful once a scan has actually happened.</summary>
    public bool IsClean => _hasRun && _report is { IsClean: true };

    public bool HasFindings => Findings.Count > 0;

    public string ScopeText => _report is null
        ? "not checked yet"
        : $"{_report.CodeModCount} mods ship code  ·  {_report.ScannedAssemblies} assemblies read  ·  "
          + $"{_report.GameMethodCount:N0} game methods";

    public string RanAtText => _report is null
        ? "never checked"
        : "checked " + _report.RanAtUtc.LocalDateTime.ToString("HH:mm:ss");

    public string? GameAssemblyError => _report?.GameAssemblyError;

    public bool HasGameAssemblyError => !string.IsNullOrEmpty(_report?.GameAssemblyError);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    /// <summary>
    /// Runs the checks off the UI thread. Reading 60-odd assemblies takes under a second,
    /// but that is still long enough to stutter a scroll.
    /// </summary>
    public async Task RunAsync(bool force = false)
    {
        if (_app.Paths is null)
        {
            StatusMessage = "Set the Elin folder in Settings first.";
            return;
        }

        if (IsRunning) return;
        if (_hasRun && !force) return;

        IsRunning = true;
        StatusMessage = null;

        try
        {
            var paths = _app.Paths;
            var scan = _app.Scan;
            var loadOrder = _app.LoadOrder;

            // Ask Steam first when the user has allowed it, so the checks that need it
            // have something to work with. A failure there is not a failure of the scan.
            if (_app.Settings.EnableWorkshopChecks)
            {
                var problem = await _app.Workshop.RefreshAsync();
                if (problem is not null) StatusMessage = problem;
            }

            var workshop = _app.Workshop.HasData ? _app.Workshop.Items : null;
            var report = await Task.Run(() =>
                _scanner.Scan(paths, scan, loadOrder, workshop, _app.Selections));

            _report = report;
            _hasRun = true;
            Show(report);
            _onChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = "The check could not finish: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Show(HealthReport report)
    {
        Findings.Clear();

        foreach (var finding in report.Findings
                     .OrderBy(f => (int)f.Severity)
                     .ThenBy(f => f.Check, StringComparer.Ordinal))
        {
            Findings.Add(new HealthFindingViewModel(this, finding));
        }

        OnPropertyChanged(nameof(BrokenCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(NoticeCount));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(RanAtText));
        OnPropertyChanged(nameof(GameAssemblyError));
        OnPropertyChanged(nameof(HasGameAssemblyError));
    }

    /// <summary>Resolves a finding's mod key against the current scan.</summary>
    public ModPackage? FindMod(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _app.Scan.Mods.FirstOrDefault(m => m.Key == key);
    }

    private void DisableMod(object? parameter)
    {
        if (parameter is not HealthModViewModel row || row.Mod is null) return;

        var result = _app.ApplyModEnabledStates(new[] { (row.Mod, false) });

        StatusMessage = result.Success
            ? $"Turned off {row.Mod.Name}. Restart Elin for it to take effect."
            : result.Message;

        if (!result.Success) return;

        foreach (var finding in Findings) finding.RefreshMods();
        DisableModCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Called after a rescan, so the page never shows findings about a stale library.</summary>
    public void Invalidate()
    {
        _hasRun = false;
        _report = null;
        Findings.Clear();
        Show(new HealthReport());
    }
}
