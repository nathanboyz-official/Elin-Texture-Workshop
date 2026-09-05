using System.IO;
using Path = System.IO.Path;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One selectable version of a texture in the comparison view.</summary>
public sealed class TextureVersionViewModel : ObservableObject
{
    private BitmapSource? _image;
    private bool _isSelectedForCompare;
    private bool _isChosen;

    public TextureVersionViewModel(
        TextureFile file,
        bool isChosen,
        bool isWinner,
        bool isIdenticalToWinner,
        bool isIdenticalToOriginal = false)
    {
        File = file;
        _isChosen = isChosen;
        IsWinner = isWinner;
        IsIdenticalToWinner = isIdenticalToWinner;
        IsIdenticalToOriginal = isIdenticalToOriginal;
        LoadImage();
    }

    public TextureFile File { get; }

    public string ModName => File.ModName;
    public string? WorkshopId => File.WorkshopId;
    public string FullPath => File.FullPath;
    public string Dimensions => File.DimensionsText;
    public bool IsVariant => File.IsVariant;
    public string? VariantName => File.VariantName;

    public string SourceKindLabel => File.SourceType switch
    {
        TextureSourceType.Override => "Your override",
        TextureSourceType.LocalMod => "Local mod",
        TextureSourceType.Vanilla => "Vanilla",
        TextureSourceType.Custom => "Added by you",
        _ => IsVariant ? $"Variant: {VariantName}" : "Workshop",
    };

    public string FileSizeText => File.FileSize switch
    {
        < 1024 => $"{File.FileSize} B",
        < 1024 * 1024 => $"{File.FileSize / 1024.0:0.#} KB",
        _ => $"{File.FileSize / (1024.0 * 1024):0.##} MB",
    };

    public string HashShort => File.Hash is null
        ? "not hashed"
        : File.Hash[..Math.Min(16, File.Hash.Length)].ToLowerInvariant();

    public string HashFull => File.Hash ?? "not hashed";

    /// <summary>True when this file is byte-identical to the currently winning one.</summary>
    public bool IsIdenticalToWinner { get; }

    /// <summary>
    /// True when this file is byte-identical to the base game's own copy - a "replacement"
    /// that replaces nothing, which is worth knowing before choosing it.
    /// </summary>
    public bool IsIdenticalToOriginal { get; }

    public bool IsVanilla => File.SourceType == TextureSourceType.Vanilla;

    public bool IsWinner { get; }

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }

    public bool IsSelectedForCompare
    {
        get => _isSelectedForCompare;
        set => SetProperty(ref _isSelectedForCompare, value);
    }

    public BitmapSource? Image
    {
        get => _image;
        private set => SetProperty(ref _image, value);
    }

    private async void LoadImage() => Image = await TextureImageLoader.LoadAsync(File.FullPath);

    public void Reload()
    {
        TextureImageLoader.Invalidate(File.FullPath);
        LoadImage();
    }
}

/// <summary>
/// The comparison page for a single texture ID: the current winner large, then every
/// installed version side by side with a SELECT button.
/// </summary>
public sealed class TextureDetailViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action _onChanged;

    private string _statusMessage = string.Empty;
    private string? _aliasDraft;
    private bool _compareMode;
    private TextureVersionViewModel? _compareA;
    private TextureVersionViewModel? _compareB;
    private bool _showB;

    public TextureDetailViewModel(AppServices app, TextureEntry entry, Action onChanged)
    {
        _app = app;
        Entry = entry;
        _onChanged = onChanged;

        _aliasDraft = app.Aliases.Get(entry.TextureId);

        SelectCommand = new RelayCommand(p => Select(p as TextureVersionViewModel));
        RemoveOverrideCommand = new RelayCommand(RemoveOverride, () => HasOverride);
        OpenSourceFolderCommand = new RelayCommand(p =>
            ShellService.RevealFile((p as TextureVersionViewModel)?.FullPath));
        ToggleCompareCommand = new RelayCommand(p => ToggleCompare(p as TextureVersionViewModel));
        ClearCompareCommand = new RelayCommand(ClearCompare);
        ToggleAbCommand = new RelayCommand(() => ShowB = !ShowB);
        SaveAliasCommand = new RelayCommand(SaveAlias);

        Rebuild();
    }

    public TextureEntry Entry { get; }

    public string TextureId => Entry.DisplayId;
    public string Category => Entry.Category;
    public string Prefix => Entry.Prefix;
    public string SourceSummary => Entry.VersionSummary;

    public List<TextureVersionViewModel> Versions { get; private set; } = new();
    public List<TextureVersionViewModel> Variants { get; private set; } = new();

    public bool HasVariants => Variants.Count > 0;

    /// <summary>
    /// The base game's own version of this image, when it ships as a loose file under
    /// Package\_Elona. Null for "Texture Replace" sprites - see <see cref="OriginalNote"/>.
    /// </summary>
    public TextureVersionViewModel? Original { get; private set; }

    public bool HasOriginal => Original is not null;

    /// <summary>
    /// Said plainly rather than left blank, because "no original shown" and "there is no
    /// original" are different things and the user cannot tell them apart otherwise.
    /// </summary>
    public string OriginalNote => Entry.Kind == ReplacementKind.Portrait
        ? "The base game has no portrait of this name, so this mod adds one rather than replacing it."
        : "Sprites in \"Texture Replace\" address a slot inside Elin's packed sprite atlas, "
          + "which is not a loose file. The original for this one cannot be shown.";

    /// <summary>True when a mod ships the base game's file byte for byte.</summary>
    public bool AnyMatchesOriginal => Original?.File.Hash is not null
                                      && Versions.Any(v => v.IsIdenticalToOriginal);

    /// <summary>
    /// The "-overlay" layer Elin draws on top of this portrait. It has no tile of its own
    /// in the grid - it is not a picture in its own right - so every version of it is
    /// offered here, selectable exactly like the portrait's own versions.
    /// </summary>
    public List<TextureVersionViewModel> OverlayVersions { get; private set; } = new();

    public bool HasOverlay => Entry.HasOverlay && OverlayVersions.Count > 0;

    /// <summary>The overlay's own ID, so it is clear which file selecting one writes.</summary>
    public string? OverlayId => Entry.Overlay?.DisplayId;

    public string OverlaySummary => Entry.Overlay is null
        ? string.Empty
        : Entry.Overlay.HasConflict
            ? $"{Entry.Overlay.SourceCount} mods supply this overlay"
            : "1 source";

    /// <summary>True when this entry is itself an overlay reached directly.</summary>
    public bool IsOverlay => Entry.IsOverlay;

    public TextureWinner Winner { get; private set; } = TextureWinner.None;

    public TextureVersionViewModel? WinnerVersion { get; private set; }

    public string WinnerLabel => Winner.Description;

    public string ConfidenceLabel => Winner.Confidence switch
    {
        WinnerConfidence.Certain => "Certain",
        WinnerConfidence.Likely => "Based on load order",
        _ => "Uncertain",
    };

    public bool IsUncertain => Winner.Confidence == WinnerConfidence.Unknown;

    public bool HasOverride => _app.Selections.Has(Entry.TextureId);

    public string? OverrideSourceName => _app.Selections.Get(Entry.TextureId)?.SourceModName;

    public bool AllIdentical => Entry.AllIdentical;

    public string? AliasDraft
    {
        get => _aliasDraft;
        set => SetProperty(ref _aliasDraft, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ---- side-by-side comparison ----

    public bool CompareMode
    {
        get => _compareMode;
        private set => SetProperty(ref _compareMode, value);
    }

    public TextureVersionViewModel? CompareA
    {
        get => _compareA;
        private set { SetProperty(ref _compareA, value); OnPropertyChanged(nameof(AbCurrent)); }
    }

    public TextureVersionViewModel? CompareB
    {
        get => _compareB;
        private set { SetProperty(ref _compareB, value); OnPropertyChanged(nameof(AbCurrent)); }
    }

    /// <summary>Which of the two the A/B toggle is currently showing.</summary>
    public bool ShowB
    {
        get => _showB;
        set { SetProperty(ref _showB, value); OnPropertyChanged(nameof(AbCurrent)); OnPropertyChanged(nameof(AbLabel)); }
    }

    public TextureVersionViewModel? AbCurrent => ShowB ? CompareB : CompareA;

    public string AbLabel => ShowB ? "Showing B" : "Showing A";

    public RelayCommand SelectCommand { get; }
    public RelayCommand RemoveOverrideCommand { get; }
    public RelayCommand OpenSourceFolderCommand { get; }
    public RelayCommand ToggleCompareCommand { get; }
    public RelayCommand ClearCompareCommand { get; }
    public RelayCommand ToggleAbCommand { get; }
    public RelayCommand SaveAliasCommand { get; }

    private void Rebuild()
    {
        Winner = _app.Winners.GetValueOrDefault(Entry.TextureId) ?? TextureWinner.None;
        var winnerHash = Winner.File?.Hash;
        var chosen = _app.Selections.Get(Entry.TextureId);
        var originalHash = Entry.Vanilla?.Hash;

        TextureVersionViewModel Build(TextureFile f) => new(
            f,
            isChosen: chosen is not null
                      && string.Equals(f.ModKey, chosen.SourceModKey, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(f.FullPath, chosen.SourcePath, StringComparison.OrdinalIgnoreCase),
            isWinner: Winner.File is not null
                      && string.Equals(f.FullPath, Winner.File.FullPath, StringComparison.OrdinalIgnoreCase),
            // Only meaningful for a *different* file that happens to be byte-identical;
            // the winner is trivially identical to itself.
            isIdenticalToWinner: winnerHash is not null
                                 && f.Hash is not null
                                 && Winner.File is not null
                                 && !string.Equals(f.FullPath, Winner.File.FullPath, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(f.Hash, winnerHash, StringComparison.OrdinalIgnoreCase),
            isIdenticalToOriginal: originalHash is not null
                                   && f.Hash is not null
                                   && f.SourceType != TextureSourceType.Vanilla
                                   && string.Equals(f.Hash, originalHash, StringComparison.OrdinalIgnoreCase));

        Versions = ChoosableVersions().Select(Build).ToList();
        Variants = Entry.Variants.Select(Build).ToList();
        Original = Entry.Vanilla is null ? null : Build(Entry.Vanilla);
        OverlayVersions = BuildOverlayVersions();
        // What the game loads is often our own override copy, which is deliberately not
        // in the choosable list - but it is exactly what this panel has to show, so it
        // is built directly from the resolved winner when the list does not hold it.
        WinnerVersion = Versions.FirstOrDefault(v => v.IsWinner)
                        ?? (Winner.File is not null ? Build(Winner.File) : null);

        OnPropertyChanged(nameof(Versions));
        OnPropertyChanged(nameof(Variants));
        OnPropertyChanged(nameof(HasVariants));
        OnPropertyChanged(nameof(Original));
        OnPropertyChanged(nameof(HasOriginal));
        OnPropertyChanged(nameof(OriginalNote));
        OnPropertyChanged(nameof(AnyMatchesOriginal));
        OnPropertyChanged(nameof(OverlayVersions));
        OnPropertyChanged(nameof(HasOverlay));
        OnPropertyChanged(nameof(OverlayId));
        OnPropertyChanged(nameof(OverlaySummary));
        OnPropertyChanged(nameof(WinnerVersion));
        OnPropertyChanged(nameof(WinnerLabel));
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(IsUncertain));
        OnPropertyChanged(nameof(HasOverride));
        OnPropertyChanged(nameof(OverrideSourceName));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(AllIdentical));
    }

    private void Select(TextureVersionViewModel? version)
    {
        if (version is null || _app.Overrides is null) return;

        var result = _app.Overrides.Select(version.File);
        StatusMessage = result.Message;

        if (!result.Success) return;

        // The override package now contains this texture; refresh so the winner and
        // the badges reflect it without waiting for a full rescan.
        _app.RecomputeWinners();
        AddOrUpdateOverrideInIndex(version.File);
        _app.RecomputeWinners();
        Rebuild();
        _onChanged();
    }

    /// <summary>
    /// Keeps the in-memory index in step with the file just written, so the UI does not
    /// need a full rescan to show the override as the winner.
    /// </summary>
    private void AddOrUpdateOverrideInIndex(TextureFile source)
    {
        if (_app.Paths is null) return;

        // Portrait overrides live in the package's Portrait folder, not Texture Replace.
        var overridePath = Path.Combine(_app.Paths.OverrideRootFor(source.Kind), source.FileName);

        var existing = Entry.Versions.FirstOrDefault(v => v.SourceType == TextureSourceType.Override);
        if (existing is not null) Entry.Versions.Remove(existing);

        var mod = _app.Scan.Mods.FirstOrDefault(m => m.SourceType == TextureSourceType.Override);

        var file = new TextureFile
        {
            FullPath = overridePath,
            FileName = source.FileName,
            Identity = source.Identity,
            RelativePath = Path.Combine(source.Kind.FolderName(), source.FileName),
            ModKey = mod?.Key ?? Core.Detection.ElinPaths.OverridePackageName,
            ModName = "Elin Texture Manager Overrides",
            SourceType = TextureSourceType.Override,
            Kind = source.Kind,
            FileSize = source.FileSize,
            LastModifiedUtc = DateTime.UtcNow,
            Hash = source.Hash,
            PixelWidth = source.PixelWidth,
            PixelHeight = source.PixelHeight,
        };

        Entry.Versions.Add(file);
        mod?.Textures.RemoveAll(t =>
            string.Equals(t.FileName, source.FileName, StringComparison.OrdinalIgnoreCase));
        mod?.Textures.Add(file);

        TextureImageLoader.Invalidate(overridePath);
    }

    private void RemoveOverride()
    {
        if (_app.Overrides is null) return;

        var result = _app.Overrides.Remove(Entry.TextureId);
        StatusMessage = result.Message;

        if (!result.Success) return;

        var existing = Entry.Versions.FirstOrDefault(v => v.SourceType == TextureSourceType.Override);
        if (existing is not null)
        {
            Entry.Versions.Remove(existing);
            TextureImageLoader.Invalidate(existing.FullPath);

            var mod = _app.Scan.Mods.FirstOrDefault(m => m.SourceType == TextureSourceType.Override);
            mod?.Textures.RemoveAll(t =>
                string.Equals(t.FullPath, existing.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        _app.RecomputeWinners();
        Rebuild();
        _onChanged();
    }

    /// <summary>
    /// What the user can actually pick between.
    ///
    /// The copy in our own override package is left out: it is a duplicate of whichever
    /// mod was chosen, and offering "use this texture" on it does nothing. The choice is
    /// already shown - the card it was copied from is marked SELECTED, and the panel
    /// above names it. The one exception is a copy whose source mod has been
    /// unsubscribed, where hiding it would leave nothing on the page at all.
    /// </summary>
    private IEnumerable<TextureFile> ChoosableVersions()
    {
        var fromMods = Entry.ModVersions.ToList();
        return fromMods.Count > 0 ? fromMods : Entry.Versions;
    }

    /// <summary>
    /// The overlay's versions, built with their own winner and override state so that
    /// selecting one behaves exactly as it would on a page of its own.
    /// </summary>
    private List<TextureVersionViewModel> BuildOverlayVersions()
    {
        var overlay = Entry.Overlay;
        if (overlay is null) return new List<TextureVersionViewModel>();

        var winner = _app.Winners.GetValueOrDefault(overlay.TextureId) ?? TextureWinner.None;
        var chosen = _app.Selections.Get(overlay.TextureId);
        var originalHash = overlay.Vanilla?.Hash;

        var files = overlay.ModVersions.Any() ? overlay.ModVersions : overlay.Versions;
        if (overlay.Vanilla is not null) files = files.Append(overlay.Vanilla);

        return files.Select(f => new TextureVersionViewModel(
                f,
                isChosen: chosen is not null
                          && string.Equals(f.FullPath, chosen.SourcePath, StringComparison.OrdinalIgnoreCase),
                isWinner: winner.File is not null
                          && string.Equals(f.FullPath, winner.File.FullPath, StringComparison.OrdinalIgnoreCase),
                isIdenticalToWinner: false,
                isIdenticalToOriginal: originalHash is not null
                                       && f.Hash is not null
                                       && f.SourceType != TextureSourceType.Vanilla
                                       && string.Equals(f.Hash, originalHash, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Everything the A/B comparison can be pointed at, the original included.</summary>
    private IEnumerable<TextureVersionViewModel> AllComparable()
    {
        foreach (var v in Versions) yield return v;
        foreach (var v in Variants) yield return v;
        foreach (var v in OverlayVersions) yield return v;
        if (Original is not null) yield return Original;
    }

    private void ToggleCompare(TextureVersionViewModel? version)
    {
        if (version is null) return;

        if (version.IsSelectedForCompare)
        {
            version.IsSelectedForCompare = false;
            if (ReferenceEquals(CompareA, version)) CompareA = null;
            if (ReferenceEquals(CompareB, version)) CompareB = null;
        }
        else
        {
            if (CompareA is null) CompareA = version;
            else if (CompareB is null) CompareB = version;
            else
            {
                // Both slots full: replace B so repeated clicks keep comparing against A.
                CompareB.IsSelectedForCompare = false;
                CompareB = version;
            }

            version.IsSelectedForCompare = true;
        }

        CompareMode = CompareA is not null && CompareB is not null;
        if (!CompareMode) ShowB = false;
    }

    private void ClearCompare()
    {
        foreach (var v in AllComparable()) v.IsSelectedForCompare = false;
        CompareA = null;
        CompareB = null;
        CompareMode = false;
        ShowB = false;
    }

    private void SaveAlias()
    {
        _app.Aliases.Set(Entry.TextureId, AliasDraft);
        _app.Aliases.Save();
        StatusMessage = string.IsNullOrWhiteSpace(AliasDraft)
            ? "Name cleared."
            : $"Named \"{AliasDraft}\".";
        _onChanged();
    }
}
