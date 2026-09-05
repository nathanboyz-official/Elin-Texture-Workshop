using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using System.IO;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Sheets;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One clickable section on the Mods page.</summary>
public sealed class ModSectionViewModel : ObservableObject
{
    private bool _isSelected;
    private int _count;

    public ModSectionViewModel(string key, string label, string group)
    {
        Key = key;
        Label = label;
        Group = group;
    }

    /// <summary>Stable key, persisted as the last-used section.</summary>
    public string Key { get; }

    public string Label { get; }

    /// <summary>"Content" for sections derived from what a mod ships, "Workshop tags" for tags.</summary>
    public string Group { get; }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>A row on the Mods page.</summary>
public sealed class ModRowViewModel : ObservableObject
{
    private BitmapSource? _preview;
    private bool _enabled;
    private bool _descriptionExpanded;

    public ModRowViewModel(ModPackage mod, int conflictCount, int uniqueCount, bool enabled)
    {
        Mod = mod;
        ConflictCount = conflictCount;
        UniqueCount = uniqueCount;
        _enabled = enabled;
        Sections = ModSection.SectionsFor(mod);
        LoadPreview();
    }

    public ModPackage Mod { get; }

    /// <summary>Raised when the user flips the switch, so the page can offer to save.</summary>
    public event Action<ModRowViewModel>? EnabledChanged;

    public string Name => Mod.Name;
    public string? WorkshopId => Mod.WorkshopId;
    public string Directory => Mod.Directory;
    public string? Author => Mod.Author;

    /// <summary>
    /// The mod's own ID from package.xml. Shown first on the detail line because it is
    /// what the mod calls itself, and it is usually more recognisable - and more
    /// searchable - than a numeric Workshop ID.
    /// </summary>
    public string PackageId => Mod.ModId ?? Mod.Name;

    public string? Description => string.IsNullOrWhiteSpace(Mod.Description) ? null : Mod.Description;

    public bool HasDescription => Description is not null;

    public bool DescriptionExpanded
    {
        get => _descriptionExpanded;
        set => SetProperty(ref _descriptionExpanded, value);
    }

    public int TextureCount => Mod.TextureCount;
    public int SpriteCount => Mod.SpriteCount;
    public int PortraitCount => Mod.PortraitCount;
    public int ConflictCount { get; }
    public int UniqueCount { get; }

    /// <summary>How many character sprites this mod replaces - objC and friends.</summary>
    public int CharacterCount => Mod.CharacterTextureCount;

    public bool ReplacesCharacters => Mod.ReplacesCharacters;

    public bool HasPortraits => Mod.PortraitCount > 0;

    public string CharacterSummary => CharacterCount == 1 ? "1 character" : CharacterCount + " characters";

    public string PortraitSummary => PortraitCount == 1 ? "1 portrait" : PortraitCount + " portraits";

    public IReadOnlyList<string> Sections { get; }

    public string SectionsText => string.Join("   ", Sections);

    public bool HasTextures => Mod.HasTextureReplacements;
    public bool MetadataMissing => Mod.MetadataMissing;

    /// <summary>Local packages are always loaded, so their switch is disabled.</summary>
    public bool CanToggle => Mod.CanToggle;

    /// <summary>
    /// The enabled state as shown, which may differ from what is on disk until the page
    /// applies its changes. Flipping this writes nothing to loadorder.txt.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            OnPropertyChanged(nameof(IsPending));
            EnabledChanged?.Invoke(this);
        }
    }

    /// <summary>True when the switch no longer matches what loadorder.txt says.</summary>
    public bool IsPending => Enabled != Mod.Enabled;

    public void RefreshPendingState() => OnPropertyChanged(nameof(IsPending));

    public string LoadOrderText => Mod.InLoadOrderFile
        ? "#" + (Mod.LoadOrderIndex + 1)
        : Mod.SourceType switch
        {
            TextureSourceType.Override => "override",
            TextureSourceType.LocalMod => "local package",
            TextureSourceType.Vanilla => "base game",
            TextureSourceType.Custom => "added by you",
            // A Workshop item that loadorder.txt does not mention: say so rather than
            // implying a position we do not have.
            _ => "not in load order",
        };

    public string LastUpdatedText => Mod.LastModifiedUtc == default
        ? "unknown"
        : Mod.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");

    public string SourceLabel => Mod.SourceType switch
    {
        TextureSourceType.Override => "Override package",
        TextureSourceType.LocalMod => "Local package",
        TextureSourceType.Vanilla => "Base game",
        TextureSourceType.Custom => @"Elin\Custom",
        _ => "Workshop",
    };

    public BitmapSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    private async void LoadPreview()
    {
        // Prefer the mod's own preview image; otherwise show one of its textures.
        var path = Mod.PreviewImagePath
                   ?? Mod.Textures.FirstOrDefault(t => !t.IsVariant)?.FullPath
                   ?? Mod.Textures.FirstOrDefault()?.FullPath;

        if (path is null) return;
        Preview = await TextureImageLoader.LoadAsync(path, 128);
    }

    /// <summary>Which content sections this mod belongs to, from what it actually ships.</summary>
    public bool InContentSection(string key) => key switch
    {
        ModsViewModel.SectionCharacters => ReplacesCharacters,
        ModsViewModel.SectionPortraits => HasPortraits,
        ModsViewModel.SectionItems => HasCategory(TextureCategory.Items),
        ModsViewModel.SectionObjects => HasCategory(TextureCategory.Objects),
        _ => false,
    };

    private bool HasCategory(string category) => Mod.Textures.Any(t =>
        t.Kind == ReplacementKind.TextureReplace
        && !t.IsVariant
        && t.Category == category);

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        return Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || PackageId.Contains(query, StringComparison.OrdinalIgnoreCase)
               || WorkshopId?.Contains(query, StringComparison.Ordinal) == true
               || Author?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
               || Mod.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The Mods page: every detected mod, arranged into the sections the Steam Workshop uses,
/// with a switch that turns a whole mod off.
/// </summary>
public sealed class ModsViewModel : ObservableObject
{
    public const string SectionAll = "All";
    public const string SectionCharacters = "Characters";
    public const string SectionPortraits = "Portraits";
    public const string SectionItems = "Items";
    public const string SectionObjects = "Objects";

    private const string TagPrefix = "tag:";

    private static readonly string[] ContentSections =
        { SectionCharacters, SectionPortraits, SectionItems, SectionObjects };

    private readonly AppServices _app;
    private readonly Action _onChanged;

    /// <summary>Switch positions the user has changed but not yet applied, by mod key.</summary>
    private readonly Dictionary<string, bool> _pending = new(StringComparer.OrdinalIgnoreCase);

    private string _searchText = string.Empty;
    private bool _texturesOnly;
    private string _section = SectionAll;
    private string? _statusMessage;

    public ModsViewModel(AppServices app, Action<ModPackage> openMod, Action onChanged)
    {
        _app = app;
        _onChanged = onChanged;

        _texturesOnly = app.Settings.ModsTexturesOnly;
        _section = string.IsNullOrWhiteSpace(app.Settings.ModsSection)
            ? SectionAll
            : app.Settings.ModsSection;

        OpenCommand = new RelayCommand(p =>
        {
            if (p is ModRowViewModel row) openMod(row.Mod);
        });

        OpenFolderCommand = new RelayCommand(p =>
            ShellService.OpenFolder((p as ModRowViewModel)?.Directory));

        OpenWorkshopPageCommand = new RelayCommand(p =>
            ShellService.OpenWorkshopPage(
                (p as ModRowViewModel)?.WorkshopId,
                _app.Settings.OpenWorkshopInSteamApp));

        ToggleDescriptionCommand = new RelayCommand(p =>
        {
            if (p is ModRowViewModel row) row.DescriptionExpanded = !row.DescriptionExpanded;
        });

        SelectSectionCommand = new RelayCommand(p =>
        {
            if (p is ModSectionViewModel s) Section = s.Key;
        });

        ApplyChangesCommand = new RelayCommand(ApplyChanges, () => HasPendingChanges);
        DiscardChangesCommand = new RelayCommand(DiscardChanges, () => HasPendingChanges);
        NewModCommand = new RelayCommand(_ => StartAMod());
        EnableAllShownCommand = new RelayCommand(() => SetAllShown(true));
        DisableAllShownCommand = new RelayCommand(() => SetAllShown(false));
    }

    public ObservableCollection<ModRowViewModel> Items { get; } = new();
    public ObservableCollection<ModSectionViewModel> Sections { get; } = new();

    public RelayCommand NewModCommand { get; }
    public RelayCommand OpenCommand { get; }

    /// <summary>
    /// Starts a mod: the folder, package.xml, and source sheets already carrying the
    /// official first three rows.
    ///
    /// The header rows are worked out from the mods already installed. The official
    /// sheets live in a Google Drive rather than in the game, so there is nothing on the
    /// machine to copy them from - but every mod that uses one was told to copy those
    /// rows in whole, so the header the most of them agree on is the official one.
    /// </summary>
    private List<SheetTemplate>? _templates;

    private async void StartAMod()
    {
        if (_app.Paths is null)
        {
            StatusMessage = "Elin has not been found yet, so there is nowhere to put a mod.";
            return;
        }

        // Reading every workbook in the library took twenty-four seconds on a real one,
        // and doing it on this thread froze the window for all of them - the button
        // looked broken rather than busy. Done once, off the thread, and kept.
        if (_templates is null)
        {
            StatusMessage = "Reading the header rows out of your installed mods...";

            try
            {
                _templates = await Task.Run(() => SourceSheetTemplates.Harvest(Workbooks()));
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not read source sheet headers", ex);
                _templates = new List<SheetTemplate>();
            }

            StatusMessage = null;
        }

        var model = new NewModViewModel(_app.Paths, _templates);

        var window = new Views.NewModWindow(model)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true) return;

        string folder;

        try
        {
            folder = NewMod.Create(_app.Paths, model.ToRequest());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not create the mod",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"Created {folder}. Add a preview.jpg to it before publishing - "
                        + "the Workshop wants one, and the game shows it in the mod list.";

        ShellService.OpenFolder(folder);
        _onChanged();
    }

    /// <summary>Every workbook in the library, which is where the header rows come from.</summary>
    private List<string> Workbooks()
    {
        var books = new List<string>();

        foreach (var mod in _app.Scan.Mods)
        {
            try
            {
                books.AddRange(Directory
                    .GetFiles(mod.Directory, "*.xlsx", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal)));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Could not look for sheets in {mod.Directory}: {ex.Message}");
            }
        }

        return books;
    }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenWorkshopPageCommand { get; }
    public RelayCommand ToggleDescriptionCommand { get; }
    public RelayCommand SelectSectionCommand { get; }
    public RelayCommand ApplyChangesCommand { get; }
    public RelayCommand DiscardChangesCommand { get; }
    public RelayCommand EnableAllShownCommand { get; }
    public RelayCommand DisableAllShownCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Apply(); }
    }

    /// <summary>Most Workshop items are not texture packs, so this defaults to on.</summary>
    public bool TexturesOnly
    {
        get => _texturesOnly;
        set
        {
            if (!SetProperty(ref _texturesOnly, value)) return;
            _app.Settings.ModsTexturesOnly = value;
            _app.SaveSettings();
            Apply();
        }
    }

    public string Section
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            _app.Settings.ModsSection = value;
            _app.SaveSettings();
            Apply();
        }
    }

    public int ResultCount => Items.Count;
    public int PendingCount => _pending.Count;
    public bool HasPendingChanges => _pending.Count > 0;

    public string PendingText => _pending.Count == 1
        ? "1 mod changed, not saved yet"
        : _pending.Count + " mods changed, not saved yet";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public bool IsEmpty => Items.Count == 0;

    public string EmptyMessage => _app.Scan.ModCount == 0
        ? "No mods found yet. Use Refresh to scan your Workshop folder."
        : "No mods in this section match the current filters.";

    /// <summary>Scroll position, kept across navigation. See TextureBrowserViewModel.</summary>
    public double ScrollOffset { get; set; }

    public void Apply(bool preserveScroll = false)
    {
        if (!preserveScroll) ScrollOffset = 0;

        foreach (var existing in Items) existing.EnabledChanged -= OnRowEnabledChanged;
        Items.Clear();

        var (conflictsByMod, uniqueByMod) = CountPerMod();

        // The candidate set the section counts are taken from. The "textures only" filter
        // applies here too, so a section never advertises mods the filter is hiding.
        var candidates = _app.Scan.Mods
            .Where(m => m.SourceType != TextureSourceType.Vanilla)
            .Where(m => !_texturesOnly || m.HasTextureReplacements)
            .Select(m => new ModRowViewModel(
                m,
                conflictsByMod.GetValueOrDefault(m.Key),
                uniqueByMod.GetValueOrDefault(m.Key),
                _pending.TryGetValue(m.Key, out var pending) ? pending : m.Enabled))
            .ToList();

        RebuildSections(candidates);

        foreach (var row in candidates
                     .Where(r => InSection(r, _section))
                     .Where(r => r.Matches(_searchText))
                     .OrderByDescending(r => r.TextureCount)
                     .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            row.EnabledChanged += OnRowEnabledChanged;
            Items.Add(row);
        }

        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        RaisePendingProperties();
    }

    /// <summary>Per-mod conflict and unique counts, taken over the whole index.</summary>
    private (Dictionary<string, int> Conflicts, Dictionary<string, int> Unique) CountPerMod()
    {
        var conflicts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unique = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _app.Scan.Index.Values)
        {
            foreach (var v in entry.Versions)
            {
                var map = entry.HasConflict ? conflicts : unique;
                map[v.ModKey] = map.GetValueOrDefault(v.ModKey) + 1;
            }
        }

        return (conflicts, unique);
    }

    /// <summary>
    /// Builds the section list from what is installed. A section with no mods is left out
    /// rather than shown empty, and the counts come from the same candidate set the list
    /// itself is drawn from, so a chip can never promise more than it delivers.
    /// </summary>
    private void RebuildSections(IReadOnlyList<ModRowViewModel> candidates)
    {
        Sections.Clear();
        Sections.Add(new ModSectionViewModel(SectionAll, "All mods", "Content")
        {
            Count = candidates.Count,
        });

        foreach (var key in ContentSections)
        {
            var count = candidates.Count(r => r.InContentSection(key));
            if (count == 0) continue;
            Sections.Add(new ModSectionViewModel(key, key, "Content") { Count = count });
        }

        foreach (var tag in ModSection.All)
        {
            var count = candidates.Count(r => r.Sections.Contains(tag, StringComparer.OrdinalIgnoreCase));
            if (count == 0) continue;
            Sections.Add(new ModSectionViewModel(TagPrefix + tag, tag, "Workshop tags") { Count = count });
        }

        // A section can disappear when its last mod is unsubscribed. Fall back to All so
        // the page is never stuck showing nothing with no way back.
        if (Sections.All(s => s.Key != _section))
        {
            _section = SectionAll;
            _app.Settings.ModsSection = SectionAll;
            OnPropertyChanged(nameof(Section));
        }

        foreach (var s in Sections) s.IsSelected = s.Key == _section;
    }

    private static bool InSection(ModRowViewModel row, string section)
    {
        if (section == SectionAll) return true;

        if (section.StartsWith(TagPrefix, StringComparison.Ordinal))
            return row.Sections.Contains(section[TagPrefix.Length..], StringComparer.OrdinalIgnoreCase);

        return row.InContentSection(section);
    }

    // ---- enabling and disabling whole mods ----

    private void OnRowEnabledChanged(ModRowViewModel row)
    {
        if (row.Enabled == row.Mod.Enabled) _pending.Remove(row.Mod.Key);
        else _pending[row.Mod.Key] = row.Enabled;

        StatusMessage = null;
        RaisePendingProperties();
    }

    private void SetAllShown(bool enabled)
    {
        foreach (var row in Items.Where(r => r.CanToggle)) row.Enabled = enabled;
    }

    private void ApplyChanges()
    {
        if (_pending.Count == 0 || _app.Paths is null) return;

        var byKey = _app.Scan.Mods.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        var changes = new List<(ModPackage Mod, bool Enabled)>();

        foreach (var pair in _pending)
            if (byKey.TryGetValue(pair.Key, out var mod)) changes.Add((mod, pair.Value));

        var off = changes.Count(c => !c.Enabled);
        var on = changes.Count - off;

        var answer = MessageBox.Show(
            "Write these changes to:\n" + _app.Paths.LoadOrderFile + "\n\n"
            + off + " mod" + (off == 1 ? "" : "s") + " off, "
            + on + " mod" + (on == 1 ? "" : "s") + " on.\n\n"
            + "A timestamped backup of the current file is taken first. "
            + "Elin picks the change up the next time it starts.",
            "Apply mod changes",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.OK);

        if (answer != MessageBoxResult.OK) return;

        var result = _app.ApplyModEnabledStates(changes);
        StatusMessage = result.Message;

        if (!result.Success) return;

        _pending.Clear();
        Apply(preserveScroll: true);
        _onChanged();
    }

    private void DiscardChanges()
    {
        _pending.Clear();
        StatusMessage = "Changes discarded.";
        Apply(preserveScroll: true);
    }

    private void RaisePendingProperties()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PendingText));
        ApplyChangesCommand.RaiseCanExecuteChanged();
        DiscardChangesCommand.RaiseCanExecuteChanged();

        foreach (var row in Items) row.RefreshPendingState();
    }
}
