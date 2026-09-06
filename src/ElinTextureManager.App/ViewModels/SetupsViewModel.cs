using System.Collections.ObjectModel;
using System.IO;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Storage;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One profile in the list.</summary>
public sealed class ProfileViewModel : ObservableObject
{
    private bool _isActive;

    public ProfileViewModel(string name, int selectionCount, bool isActive)
    {
        Name = name;
        SelectionCount = selectionCount;
        _isActive = isActive;
    }

    public string Name { get; }
    public int SelectionCount { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string DetailText => SelectionCount == 1
        ? "1 chosen texture"
        : $"{SelectionCount} chosen textures";
}

/// <summary>
/// The Setups page: named profiles, and setup files that move a whole configuration
/// between machines.
///
/// A setup file says what to do rather than carrying anything: Workshop IDs, texture
/// IDs and on/off states. Importing one subscribes to nothing and downloads nothing,
/// and says plainly which mods are not on this machine.
/// </summary>
public sealed class SetupsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onChanged;

    private string _newProfileName = string.Empty;
    private string? _statusMessage;
    private bool _isBusy;

    public SetupsViewModel(AppServices app, Action onChanged)
    {
        _app = app;
        _onChanged = onChanged;

        SwitchCommand = new RelayCommand(p => Switch(p as ProfileViewModel), _ => !IsBusy);
        CreateCommand = new RelayCommand(_ => Create(copyCurrent: false), _ => CanCreate);
        DuplicateCommand = new RelayCommand(_ => Create(copyCurrent: true), _ => CanCreate);
        DeleteCommand = new RelayCommand(p => Delete(p as ProfileViewModel), _ => Profiles.Count > 1);
        ExportCommand = new RelayCommand(_ => Export());
        ImportCommand = new RelayCommand(_ => Import());
    }

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    public RelayCommand SwitchCommand { get; }
    public RelayCommand CreateCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }

    public string ActiveProfile => _app.Selections.ActiveProfile;

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            SetProperty(ref _newProfileName, value);
            OnPropertyChanged(nameof(CanCreate));
            CreateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanCreate => !string.IsNullOrWhiteSpace(_newProfileName)
                             && !_app.Selections.HasProfile(_newProfileName.Trim());

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !_isBusy;

    public string ModSummary
    {
        get
        {
            var toggleable = _app.Scan.Mods.Count(m => m.CanToggle);
            var on = _app.Scan.Mods.Count(m => m.CanToggle && m.Enabled);
            return $"{on} of {toggleable} Workshop mods on";
        }
    }

    public void Apply()
    {
        Profiles.Clear();

        foreach (var name in _app.Selections.Profiles.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            Profiles.Add(new ProfileViewModel(name, _app.Selections.CountIn(name), name == ActiveProfile));

        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(ModSummary));
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private void Switch(ProfileViewModel? profile)
    {
        if (profile is null || profile.Name == ActiveProfile) return;

        IsBusy = true;
        try
        {
            var result = _app.SwitchProfile(profile.Name);
            StatusMessage = result.Success
                ? $"Now on {profile.Name}. {result.Message}"
                : result.Message;
        }
        finally
        {
            IsBusy = false;
        }

        Apply();
        _onChanged();
    }

    private void Create(bool copyCurrent)
    {
        var name = NewProfileName.Trim();

        if (!_app.Selections.CreateProfile(name, copyCurrent ? ActiveProfile : null))
        {
            StatusMessage = $"There is already a profile called {name}.";
            return;
        }

        _app.Selections.Save();
        NewProfileName = string.Empty;
        StatusMessage = copyCurrent
            ? $"Created {name} as a copy of {ActiveProfile}. Switch to it when you want it."
            : $"Created {name}, empty. Switch to it when you want it.";

        Apply();
    }

    private void Delete(ProfileViewModel? profile)
    {
        if (profile is null) return;

        if (!_app.Selections.DeleteProfile(profile.Name))
        {
            StatusMessage = "That is the only profile left, so it has to stay.";
            return;
        }

        _app.Selections.Save();

        // Deleting the one in use leaves the package holding its textures, so bring the
        // files back in line with whichever profile the store fell back to.
        var result = _app.ApplyActiveProfile();

        StatusMessage = $"Deleted {profile.Name}. Now on {ActiveProfile}. {result.Message}";
        Apply();
        _onChanged();
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save this setup",
            FileName = $"elin-setup-{ActiveProfile}-{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "Setup files|*.json|All files|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var document = SetupFile.Build(_app.Scan.Mods, _app.Selections.All(), ActiveProfile);
            File.WriteAllText(dialog.FileName, SetupFile.Write(document));

            StatusMessage = $"Saved {document.Mods.Count} mod states and "
                            + $"{document.Selections.Count} texture choices to "
                            + Path.GetFileName(dialog.FileName)
                            + ". It holds IDs and choices, not copies of anyone's art.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not write that file: " + ex.Message;
        }
    }

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a setup file",
            Filter = "Setup files|*.json|All files|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        SetupDocument? document;
        try
        {
            document = SetupFile.Read(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not read that file: " + ex.Message;
            return;
        }

        if (document is null)
        {
            StatusMessage = "That file is not a setup this can read.";
            return;
        }

        var plan = SetupFile.Plan(document, _app.Scan.Mods);

        IsBusy = true;
        try
        {
            // The profile in the file is created rather than merged into: importing into
            // a profile you had built yourself would quietly overwrite your own choices.
            var target = UniqueProfileName(document.Profile);
            _app.Selections.CreateProfile(target);
            _app.Selections.ReplaceProfile(target, plan.Applicable);
            _app.Selections.Save();

            var toggles = SetupFile.EnabledChanges(document, _app.Scan.Mods);
            var toggleResult = toggles.Count > 0
                ? _app.ApplyModEnabledStates(toggles)
                : new ModToggleResult(true, 0, null, null);

            var applyResult = _app.SwitchProfile(target);

            StatusMessage = Summarise(target, plan, toggleResult, applyResult);
        }
        finally
        {
            IsBusy = false;
        }

        Apply();
        _onChanged();
    }

    /// <summary>
    /// Describes what actually happened, including what did not. A setup from someone
    /// else usually names mods you have never subscribed to, and hiding that would leave
    /// the profile looking applied when half of it could not be.
    /// </summary>
    private static string Summarise(string profile, SetupImportReport plan,
        ModToggleResult toggles, ProfileApplyResult apply)
    {
        var parts = new List<string> { $"Imported as {profile}." };

        if (toggles.Changed > 0) parts.Add($"{toggles.Changed} mods switched on or off.");
        parts.Add(apply.Message);

        if (plan.MissingMods.Count > 0)
        {
            var named = string.Join(", ", plan.MissingMods.Take(4));
            var rest = plan.MissingMods.Count > 4 ? $" and {plan.MissingMods.Count - 4} more" : "";
            parts.Add($"Not on this machine: {named}{rest}. Subscribe to them on the "
                      + "Workshop and import again.");
        }

        if (!toggles.Success) parts.Add(toggles.Message);

        return string.Join(" ", parts);
    }

    private string UniqueProfileName(string wanted)
    {
        var name = string.IsNullOrWhiteSpace(wanted) ? "Imported" : wanted.Trim();
        if (!_app.Selections.HasProfile(name)) return name;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{name} ({i})";
            if (!_app.Selections.HasProfile(candidate)) return candidate;
        }

        return $"{name} ({DateTime.Now:HHmmss})";
    }
}
