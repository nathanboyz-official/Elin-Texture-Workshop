using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Pcc;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One installed part, offered for a slot.</summary>
public sealed class PccPartViewModel : ObservableObject
{
    private readonly Func<int> _direction;

    private BitmapSource? _thumbnail;
    private int _renderedFor = -1;
    private bool _isChosen;
    private bool _isFavourite;

    public PccPartViewModel(PccFile part, Func<int> direction)
    {
        Part = part;
        _direction = direction;
    }

    /// <summary>The empty choice, drawn as a "no entry" sign rather than a picture.</summary>
    public static PccPartViewModel None(string layer) =>
        new(new PccFile(layer, string.Empty, string.Empty, string.Empty, string.Empty), () => 0)
        {
            IsNone = true,
        };

    public PccFile Part { get; }

    public bool IsNone { get; private init; }

    public string Id => IsNone ? "None" : Part.Id;
    public string ModName => IsNone ? "wear nothing here" : Part.ModName;
    public string Layer => Part.Layer;

    /// <summary>Marks the hairstyles that bring a back piece, since it cannot be chosen apart.</summary>
    public bool HasBack => Part.HasBack;

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }

    /// <summary>Made in this application, so it can be deleted from here.</summary>
    public bool IsMine => !IsNone && Part.IsMine;

    public bool IsFavourite
    {
        get => _isFavourite;
        set => SetProperty(ref _isFavourite, value);
    }

    /// <summary>The star is not offered on the empty choice; there is nothing to star.</summary>
    public bool CanFavourite => !IsNone;

    /// <summary>
    /// The part's own front-facing cell rather than its whole sheet: sixteen poses at
    /// tile size reads as noise.
    /// </summary>
    public BitmapSource? Thumbnail
    {
        get { Ensure(); return _thumbnail; }
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>
    /// Redraws the tile when the character turns, so the parts on offer face the same
    /// way as the character does.
    ///
    /// Only asks for it; the work happens when the binding next reads Thumbnail, which
    /// for a virtualised grid means only the tiles actually on screen.
    /// </summary>
    public void FacingChanged()
    {
        if (IsNone || _renderedFor == _direction()) return;

        _renderedFor = -1;
        OnPropertyChanged(nameof(Thumbnail));
    }

    private async void Ensure()
    {
        if (IsNone) return;

        var direction = _direction();
        if (_renderedFor == direction) return;

        _renderedFor = direction;

        var path = Part.FullPath;
        var layer = Part.Layer;

        var rendered = await Task.Run(() =>
        {
            var sheet = PixelDecoder.Decode(path);
            if (sheet is null || !PccComposer.LooksLikeSheet(sheet)) return null;

            var piece = new PccPiece { Layer = layer, Sheet = sheet };
            return PixelDecoder.ToBitmap(PccComposer.Compose(new[] { piece }, direction, 0, scale: 3));
        });

        // A newer facing may have been asked for while this one was decoding.
        if (_renderedFor == direction) Thumbnail = rendered;
    }
}

/// <summary>One row of the editor, matching a row in the game's own.</summary>
public sealed class DressUpSlotViewModel : ObservableObject
{
    public DressUpSlotViewModel(PccSlot slot, int available)
    {
        Slot = slot;
        Available = available;
    }

    public PccSlot Slot { get; }
    public int Available { get; }

    public string Label => Slot.Label;
    public string Layer => Slot.Layer;

    public string? ChosenId { get; private set; }
    public string? ChosenMod { get; private set; }
    public string? Colour { get; private set; }

    public bool HasChoice => ChosenId is not null;

    /// <summary>Shown under the label, the way the game shows the part's id there.</summary>
    public string Detail => ChosenId is null
        ? $"{Available} available"
        : ChosenMod is null ? ChosenId : $"{ChosenId}  ·  {ChosenMod}";

    public Brush Swatch => Colour is null
        ? Brushes.Transparent
        : new SolidColorBrush(FromHex(Colour));

    public bool HasColour => Colour is not null;

    public void Show(string? id, string? mod, string? colour)
    {
        ChosenId = id;
        ChosenMod = mod;
        Colour = colour;

        OnPropertyChanged(nameof(ChosenId));
        OnPropertyChanged(nameof(ChosenMod));
        OnPropertyChanged(nameof(Colour));
        OnPropertyChanged(nameof(HasChoice));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Swatch));
        OnPropertyChanged(nameof(HasColour));
    }

    public static Color FromHex(string hex) => Color.FromRgb(
        System.Convert.ToByte(hex[..2], 16),
        System.Convert.ToByte(hex[2..4], 16),
        System.Convert.ToByte(hex[4..], 16));
}

/// <summary>A saved character from the game's own folder.</summary>
public sealed class SavedStyleViewModel
{
    public SavedStyleViewModel(PccStyle style, int missing)
    {
        Style = style;
        Missing = missing;
    }

    public PccStyle Style { get; }
    public int Missing { get; }

    public string Name => Style.Name;

    public string Detail => Missing == 0
        ? $"{Style.Parts.Count} parts"
        : $"{Style.Parts.Count} parts  ·  {Missing} not installed";

    public bool IsComplete => Missing == 0;
}

/// <summary>One dye colour on offer.</summary>
public sealed class DyeViewModel
{
    public DyeViewModel(string? hex) => Hex = hex;

    /// <summary>Null means the part is drawn as its author coloured it.</summary>
    public string? Hex { get; }

    /// <summary>True for colours the user added, which are the only removable ones.</summary>
    public bool CanForget { get; init; }

    public string Label => Hex is null ? "as drawn" : "#" + Hex;

    public Brush Swatch => Hex is null
        ? Brushes.Transparent
        : new SolidColorBrush(DressUpSlotViewModel.FromHex(Hex));

    public bool IsNone => Hex is null;
}

/// <summary>
/// The character editor: builds an Elin character from the PCC parts across every
/// installed mod, and saves it where the game's own editor will find it.
///
/// The slots, their order and their labels are the game's, so what is called Sub Hair
/// here is called Sub Hair there. The file it writes is the file the game's Export
/// button writes, which is what makes a character built here loadable in game rather
/// than merely viewable.
/// </summary>
public sealed class DressUpViewModel : ObservableObject
{
    /// <summary>
    /// Dye colours. The first is "as authored"; the rest are the muted tones Elin's own
    /// palette offers, which suit parts drawn to be shaded rather than replaced.
    /// </summary>
    private static readonly string?[] Palette =
    {
        null, "FFFFFF", "C8C8C8", "92847B", "6F6E64", "54534E", "3A3A38",
        "7C8FAE", "5E626C", "8A8F94", "6A7C6E", "9E824D", "B08A6E",
        "A86A6A", "8B5E7C", "5E4C60",
    };

    private readonly AppServices _app;
    private readonly DispatcherTimer _animation;
    private readonly Random _random = new();

    private PccLibrary _library = new();
    private PccStyle _style = new();

    private DressUpSlotViewModel? _selectedSlot;
    private BitmapSource? _preview;
    private string? _statusMessage;
    private string _characterName = "New character";
    private int _frame;
    private int _direction;
    private double _walkSpeed = 5;
    private bool _animate = true;

    public DressUpViewModel(AppServices app)
    {
        _app = app;

        SelectSlotCommand = new RelayCommand(p => SelectSlot(p as DressUpSlotViewModel));
        ChoosePartCommand = new RelayCommand(p => Choose(p as PccPartViewModel));
        ClearSlotCommand = new RelayCommand(_ => Choose(null));
        ToggleCreationsCommand = new RelayCommand(_ => CreationsShown = !CreationsShown);
        ChooseDyeCommand = new RelayCommand(p => Dye(p as DyeViewModel));
        RandomiseCommand = new RelayCommand(_ => Randomise());
        RandomColoursCommand = new RelayCommand(_ => RandomColours());
        RandomSlotColourCommand = new RelayCommand(_ => RandomSlotColour());
        RandomSlotPartCommand = new RelayCommand(_ => RandomSlotPart());
        PickFromScreenCommand = new AsyncRelayCommand(PickFromScreen);
        SaveColourCommand = new RelayCommand(_ => SaveColour(), _ => SlotColour is not null);
        ForgetColourCommand = new RelayCommand(p => ForgetColour(p as DyeViewModel));
        NewSpriteCommand = new RelayCommand(_ => NewSprite(), _ => SelectedSlot is not null);
        EditPartCommand = new RelayCommand(p => EditPart(p as PccPartViewModel),
            p => p is PccPartViewModel { IsNone: false });
        ToggleFavouriteCommand = new RelayCommand(p => ToggleFavourite(p as PccPartViewModel));
        DeletePartCommand = new RelayCommand(p => DeletePart(p as PccPartViewModel),
            p => p is PccPartViewModel { IsMine: true });
        NewCharacterCommand = new RelayCommand(_ => NewCharacter());
        LoadStyleCommand = new RelayCommand(p => Load(p as SavedStyleViewModel));
        SaveCommand = new RelayCommand(_ => Save(), _ => CanSave);
        SaveToSlotCommand = new RelayCommand(p => SaveToFavourite(p as string));
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        RefreshStylesCommand = new RelayCommand(_ => LoadSavedStyles());
        FaceCommand = new RelayCommand(p =>
        {
            if (int.TryParse(p as string, out var d)) Direction = d;
        });
        // Turning follows the circle round the character rather than the order the rows
        // sit in the file, so an arrow never flips straight from one profile to the other.
        TurnCommand = new RelayCommand(p =>
        {
            if (int.TryParse(p as string, out var by)) Direction = PccFacing.Turn(Direction, by);
        });

        _animation = new DispatcherTimer();
        _animation.Tick += (_, _) => { if (_animate) Frame = (Frame + 1) % PccComposer.Columns; };
        ApplyWalkSpeed();

        foreach (var hex in Palette) Dyes.Add(new DyeViewModel(hex));
        RefreshSavedDyes();
    }

    public ObservableCollection<DressUpSlotViewModel> Slots { get; } = new();
    public ObservableCollection<PccPartViewModel> Parts { get; } = new();

    /// <summary>Parts made in this application, kept in their own section above the rest.</summary>
    public ObservableCollection<PccPartViewModel> Creations { get; } = new();

    public bool HasCreations => Creations.Count > 0;

    private bool _creationsShown = true;

    /// <summary>
    /// Whether the Creations section is open. Clicking its heading folds it away: once a
    /// few parts have been made they push the installed ones off the screen, and they are
    /// not always what is being looked for.
    /// </summary>
    public bool CreationsShown
    {
        get => _creationsShown;
        set
        {
            if (SetProperty(ref _creationsShown, value))
                OnPropertyChanged(nameof(CreationsChevron));
        }
    }

    /// <summary>Points down when the section is open, right when it is folded away.</summary>
    public string CreationsChevron => _creationsShown ? "▾" : "▸";
    public ObservableCollection<SavedStyleViewModel> SavedStyles { get; } = new();
    public ObservableCollection<DyeViewModel> Dyes { get; } = new();

    /// <summary>Colours the user kept, shown after the built-in ones under their own heading.</summary>
    public ObservableCollection<DyeViewModel> SavedDyes { get; } = new();

    public bool HasSavedDyes => SavedDyes.Count > 0;

    public RelayCommand SelectSlotCommand { get; }
    public RelayCommand ChoosePartCommand { get; }
    public RelayCommand ClearSlotCommand { get; }
    public RelayCommand ToggleCreationsCommand { get; }
    public RelayCommand ChooseDyeCommand { get; }
    public RelayCommand RandomiseCommand { get; }
    public RelayCommand RandomColoursCommand { get; }
    public RelayCommand RandomSlotColourCommand { get; }
    public RelayCommand RandomSlotPartCommand { get; }
    public AsyncRelayCommand PickFromScreenCommand { get; }
    public RelayCommand SaveColourCommand { get; }
    public RelayCommand ForgetColourCommand { get; }
    public RelayCommand NewSpriteCommand { get; }
    public RelayCommand EditPartCommand { get; }
    public RelayCommand ToggleFavouriteCommand { get; }
    public RelayCommand DeletePartCommand { get; }
    public RelayCommand NewCharacterCommand { get; }
    public RelayCommand LoadStyleCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveToSlotCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RefreshStylesCommand { get; }
    public RelayCommand FaceCommand { get; }
    public RelayCommand TurnCommand { get; }

    public DressUpSlotViewModel? SelectedSlot
    {
        get => _selectedSlot;
        private set
        {
            SetProperty(ref _selectedSlot, value);
            OnPropertyChanged(nameof(PartsTitle));
            OnPropertyChanged(nameof(NewSpriteLabel));
            NewSpriteCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Names the kind the New button will make, so it is never a guess.</summary>
    public string NewSpriteLabel => _selectedSlot is null
        ? "New sprite"
        : $"New {_selectedSlot.Label.ToLowerInvariant()} sprite";

    public string PartsTitle => _selectedSlot is null
        ? "PICK A SLOT"
        : $"{_selectedSlot.Label.ToUpperInvariant()}  -  {Parts.Count} PARTS";

    public BitmapSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public string CharacterName
    {
        get => _characterName;
        set { SetProperty(ref _characterName, value); OnPropertyChanged(nameof(CanSave)); }
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(_characterName) && _app.Paths is not null;

    public int Frame
    {
        get => _frame;
        private set { SetProperty(ref _frame, value); Render(); }
    }

    /// <summary>
    /// Which way the character faces, 0 to 3.
    ///
    /// Turning the character turns the parts on offer with it, because judging a
    /// hairstyle by its front when the character is facing away is guesswork.
    /// </summary>
    public int Direction
    {
        get => _direction;
        set
        {
            var wrapped = ((value % PccComposer.Rows) + PccComposer.Rows) % PccComposer.Rows;
            if (!SetProperty(ref _direction, wrapped)) return;

            foreach (var part in Parts) part.FacingChanged();

            OnPropertyChanged(nameof(FacingName));
            OnPropertyChanged(nameof(IsFacingFront));
            OnPropertyChanged(nameof(IsFacingLeft));
            OnPropertyChanged(nameof(IsFacingRight));
            OnPropertyChanged(nameof(IsFacingBack));
            Render();
        }
    }

    public string FacingName => PccFacing.NameOf(Direction);

    /// <summary>
    /// The facing buttons, bound both ways so the arrows and the buttons cannot
    /// disagree about which way the character is looking.
    /// </summary>
    public bool IsFacingFront
    {
        get => Direction == PccFacing.Front;
        set { if (value) Direction = PccFacing.Front; }
    }

    public bool IsFacingLeft
    {
        get => Direction == PccFacing.Left;
        set { if (value) Direction = PccFacing.Left; }
    }

    public bool IsFacingRight
    {
        get => Direction == PccFacing.Right;
        set { if (value) Direction = PccFacing.Right; }
    }

    public bool IsFacingBack
    {
        get => Direction == PccFacing.Back;
        set { if (value) Direction = PccFacing.Back; }
    }

    public bool Animate
    {
        get => _animate;
        set
        {
            SetProperty(ref _animate, value);
            if (!value) { _frame = 0; OnPropertyChanged(nameof(Frame)); Render(); }
        }
    }

    /// <summary>
    /// Steps per second, 1 to 10. The game lets you slow the walk down to look at a
    /// costume properly, and a four-frame cycle at full speed is too quick to judge one.
    /// </summary>
    public double WalkSpeed
    {
        get => _walkSpeed;
        set { SetProperty(ref _walkSpeed, value); ApplyWalkSpeed(); }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public int TotalParts { get; private set; }

    public string ScopeText => $"{TotalParts:N0} parts  ·  {SavedStyles.Count} saved characters";

    private void ApplyWalkSpeed() =>
        _animation.Interval = TimeSpan.FromMilliseconds(1000 / Math.Clamp(_walkSpeed, 1, 10));

    // ---- loading ----

    /// <summary>Indexes every installed part and reads the characters already saved.</summary>
    public void Apply()
    {
        if (_library.Count > 0) { _animation.Start(); return; }

        BuildLibrary();

        Slots.Clear();
        foreach (var slot in PccSlots.All)
            Slots.Add(new DressUpSlotViewModel(slot, _library.InLayer(slot.Layer).Count));

        LoadSavedStyles();
        NewCharacter();

        SelectSlot(Slots.FirstOrDefault(s => s.Layer == "hair") ?? Slots.FirstOrDefault());
        _animation.Start();
    }

    public void Suspend() => _animation.Stop();

    /// <summary>
    /// Indexes the parts by layer, set and id - the three things a style names.
    ///
    /// The set is the folder under Actor/PCC, which is why it comes from the path rather
    /// than the file name: pcc_body_2.png in female and in common are different parts.
    /// </summary>
    private void BuildLibrary()
    {
        _library = new PccLibrary();

        var files = _app.Scan.Index.Values
            .SelectMany(e => e.Versions)
            .Where(v => v.Kind == ReplacementKind.Pcc)
            .GroupBy(v => v.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var file in files)
        {
            var layer = PccLayer.Of(file.FileName);
            var id = PccLayer.UniqueIdOf(file.FileName);
            if (layer is null || string.IsNullOrEmpty(id)) continue;

            _library.Add(layer, SetOf(file), id, file.FullPath, file.ModName,
                isVanilla: file.SourceType == TextureSourceType.Vanilla,
                isMine: IsOurs(file.FullPath));
        }

        // And the base game's own parts.
        //
        // The mod scan deliberately skips built-in packages, which is right for a
        // texture browser and wrong here: almost every saved character is mostly vanilla,
        // and without these a user's own characters read as "not installed". Added after
        // the mods so that a mod replacing a vanilla part still wins, exactly as the game
        // would load it.
        IndexVanilla();

        TotalParts = _library.Count;
        OnPropertyChanged(nameof(TotalParts));
    }

    private void IndexVanilla()
    {
        if (_app.Paths is null) return;

        var root = Path.Combine(_app.Paths.VanillaPackageRoot, "Actor", "PCC");
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var setDir in Directory.GetDirectories(root))
            foreach (var file in Directory.GetFiles(setDir, "pcc_*.png"))
            {
                var name = Path.GetFileName(file);
                var layer = PccLayer.Of(name);
                var id = PccLayer.UniqueIdOf(name);
                if (layer is null || string.IsNullOrEmpty(id)) continue;

                _library.Add(layer, Path.GetFileName(setDir), id, file, "Elin", isVanilla: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not index the base game's PCC parts", ex);
        }
    }

    /// <summary>
    /// Whether parts from this set can be worn by an ordinary character.
    ///
    /// Only "female", which sounds like a restriction and is not: the game keeps every
    /// character's parts there whatever their gender, and all seventeen of the styles
    /// Elin itself saved on this machine name that set and no other. The rest are
    /// special cases - "unique" holds bodies for named gods and NPCs, "ride" holds
    /// mounts - and a style naming one of those is not something the game has ever been
    /// seen to write, so offering it here would be inventing a format and hoping.
    /// Styles that already name another set still load; they simply cannot be built.
    /// </summary>
    private static bool IsWearable(string set) =>
        string.Equals(set, PccSlots.DefaultSet, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a file was written by this application, which is what makes it ours to
    /// delete. Decided by where it lives rather than by a name anyone could type.
    /// </summary>
    private bool IsOurs(string path) =>
        _app.Paths is not null
        && path.StartsWith(_app.Paths.OverridePackageRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder under Actor/PCC a file sits in, defaulting to what styles say.</summary>
    private static string SetOf(TextureFile file)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(file.FullPath));
        return string.IsNullOrEmpty(folder) ? PccSlots.DefaultSet : folder;
    }

    /// <summary>Reads the characters saved by the game's own editor.</summary>
    private void LoadSavedStyles()
    {
        SavedStyles.Clear();

        if (_app.Paths is null) return;

        foreach (var style in PccStyleFile.ReadFolder(_app.Paths.ElinRoot))
        {
            var (_, missing) = _library.ResolveStyle(style);
            SavedStyles.Add(new SavedStyleViewModel(style, missing.Count));
        }

        OnPropertyChanged(nameof(ScopeText));
    }

    // ---- editing ----

    private void SelectSlot(DressUpSlotViewModel? slot)
    {
        if (slot is null) return;

        SelectedSlot = slot;
        Parts.Clear();
        Creations.Clear();

        var chosen = _style.Get(slot.Layer)?.FileId;

        // "None" first, always, except for the body: a character with no body is not a
        // character. Nothing sorts above it.
        if (slot.Layer != PccSlots.BodyLayer)
        {
            var none = PccPartViewModel.None(slot.Layer);
            none.IsChosen = chosen is null;
            Parts.Add(none);
        }

        var made = _library.InLayer(slot.Layer)
            .Where(p => IsWearable(p.Set))
            .Select(p => Tile(p, slot, chosen))
            .ToList();

        // Then the starred ones, then everything else. Sorting by mod keeps a mod's
        // parts together, which is how people think of them.
        foreach (var tile in made
                     .Where(t => !t.IsMine)
                     .OrderByDescending(t => t.IsFavourite)
                     .ThenBy(t => t.ModName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            Parts.Add(tile);
        }

        foreach (var tile in made
                     .Where(t => t.IsMine)
                     .OrderByDescending(t => t.IsFavourite)
                     .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            Creations.Add(tile);
        }

        OnPropertyChanged(nameof(PartsTitle));
        OnPropertyChanged(nameof(SlotColour));
        OnPropertyChanged(nameof(HasCreations));
    }

    private PccPartViewModel Tile(PccFile part, DressUpSlotViewModel slot, string? chosen)
    {
        var back = slot.Slot.BackLayer is not null
                   && _library.Resolve(slot.Slot.BackLayer,
                       new PccChoice { Set = part.Set, Id = part.Id }) is not null;

        return new PccPartViewModel(part with { HasBack = back }, () => Direction)
        {
            IsChosen = string.Equals(part.Id, chosen, StringComparison.OrdinalIgnoreCase),
            IsFavourite = _app.Settings.FavouriteParts.Contains(
                PccLibrary.KeyOf(part.Layer, part.Set, part.Id), StringComparer.OrdinalIgnoreCase),
        };
    }

    private void Choose(PccPartViewModel? part)
    {
        var slot = SelectedSlot;
        if (slot is null) return;

        // The None tile and the "Use none" button are the same action.
        if (part is { IsNone: true }) part = null;

        if (part is null)
        {
            // A character with no body is not a character, so that slot cannot be emptied.
            if (slot.Layer == PccSlots.BodyLayer)
            {
                StatusMessage = "A character always needs a body.";
                return;
            }

            _style.Set(slot.Layer, null);
        }
        else
        {
            // The colour already on the slot is kept, so trying hairstyles does not
            // reset the hair colour every time.
            var colour = _style.Get(slot.Layer)?.Colour;
            _style.Set(slot.Layer, new PccChoice
            {
                Set = part.Part.Set, Id = part.Part.Id, Colour = colour,
            });
        }

        foreach (var p in Parts) p.IsChosen = ReferenceEquals(p, part);

        OnPropertyChanged(nameof(SlotColour));

        RefreshSlot(slot);
        Render();
    }

    private void Dye(DyeViewModel? dye) => ApplyColour(dye?.Hex, dye is not null);

    /// <summary>
    /// The colour of the slot being edited, bound to the wheel.
    ///
    /// Setting it dyes that slot, which is what makes the wheel feel live: the character
    /// changes while the colour is being dragged for, not once it is let go.
    /// </summary>
    public string? SlotColour
    {
        get => SelectedSlot is null ? null : _style.Get(SelectedSlot.Layer)?.Colour;
        set { if (value is not null) ApplyColour(value, true); }
    }

    private void ApplyColour(string? hex, bool deliberate)
    {
        var slot = SelectedSlot;
        if (slot is null || !deliberate) return;

        if (_style.Get(slot.Layer) is not { } choice)
        {
            StatusMessage = $"Pick a {slot.Label.ToLowerInvariant()} first, then a colour for it.";
            return;
        }

        if (string.Equals(choice.Colour, hex, StringComparison.OrdinalIgnoreCase)) return;

        choice.Colour = hex;
        RefreshSlot(slot);
        OnPropertyChanged(nameof(SlotColour));
        Render();
    }

    /// <summary>Re-dyes everything the character is wearing, leaving the parts alone.</summary>
    private void RandomColours()
    {
        foreach (var (_, choice) in _style.Parts) choice.Colour = PccColour.RandomHex(_random);

        RefreshAllSlots();
        OnPropertyChanged(nameof(SlotColour));
        Render();
    }

    private void RandomSlotColour() => ApplyColour(PccColour.RandomHex(_random), true);

    /// <summary>Takes a colour from anywhere on screen, including from the game itself.</summary>
    private async Task PickFromScreen()
    {
        if (SelectedSlot is null) return;

        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is null) return;

        var picked = await ScreenColourPicker.PickAsync(owner);
        if (picked is null) return;

        ApplyColour(picked, true);
        StatusMessage = $"Picked #{picked} off the screen.";
    }

    /// <summary>Keeps the slot's colour, so it is there next time and next launch.</summary>
    private void SaveColour()
    {
        if (SlotColour is not { } hex) return;

        if (_app.Settings.SavedColours.Any(c =>
                string.Equals(c, hex, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"#{hex} is already saved.";
            return;
        }

        _app.Settings.SavedColours.Add(hex);
        _app.SaveSettings();

        RefreshSavedDyes();
        StatusMessage = $"Saved #{hex}.";
    }

    /// <summary>
    /// Removes a kept colour. Only ever a kept one: the built-in palette has no remove,
    /// because a user who empties it has no way to put it back.
    /// </summary>
    private void ForgetColour(DyeViewModel? dye)
    {
        if (dye?.Hex is not { } hex || !dye.CanForget) return;

        _app.Settings.SavedColours.RemoveAll(c =>
            string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));

        _app.SaveSettings();
        RefreshSavedDyes();
        StatusMessage = $"Removed #{hex} from your saved colours.";
    }

    /// <summary>
    /// Opens the sprite editor on a copy of a part, with the character being built shown
    /// behind it so the drawing is done in place rather than in the abstract.
    /// </summary>
    /// <summary>
    /// Starts a sprite from nothing, for the slot being looked at.
    ///
    /// The slot rail is already the answer to "what kind": a new Hair is made while
    /// looking at Hair. The character behind is still drawn, so a first stroke lands
    /// against the head it has to sit on rather than in an empty square.
    /// </summary>
    private void NewSprite()
    {
        if (SelectedSlot is not { } slot || _app.Paths is null) return;

        var blank = new PccFile(slot.Layer, PccSlots.DefaultSet, string.Empty, string.Empty, "new");

        OpenEditor(blank, PccSheet.Blank(), slot, suggestedName: $"my{slot.Layer}");
    }

    private void EditPart(PccPartViewModel? part)
    {
        if (part is null or { IsNone: true } || _app.Paths is null) return;
        if (SelectedSlot is not { } editing) return;

        var decoded = PixelDecoder.Decode(part.Part.FullPath);
        if (decoded is null || PccSheet.From(decoded) is not { } sheet)
        {
            StatusMessage = $"{part.Id} is not laid out as a sheet, so it cannot be edited here.";
            return;
        }

        OpenEditor(part.Part, sheet, editing, suggestedName: part.Part.Id);
    }

    /// <summary>Opens the editor and takes in whatever it saves.</summary>
    private void OpenEditor(PccFile source, PccSheet sheet, DressUpSlotViewModel slot,
        string suggestedName)
    {
        if (_app.Paths is null) return;

        var part = source;

        // What the character is actually wearing, so the editor can offer to take any of
        // it off - a hat sitting over the hair being drawn is worse than no reference.
        var worn = PccSlots.All
            .Where(s => _style.Get(s.Layer) is not null)
            .Select(s => (s.Layer, s.Label))
            .ToList();

        var editor = new SpriteEditorViewModel(_app, part, sheet, Preview,
            id => _library.Resolve(part.Layer, new PccChoice { Set = part.Set, Id = id }) is not null
                  || PccPartWriter.Exists(_app.Paths, part.Set, part.Layer, id),
            (direction, frame, hidden) => ComposeAsync(direction, frame, scale: 10, hidden),
            worn,
            suggestedName);

        var window = new Views.SpriteEditorWindow(editor)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        if (window.ShowDialog() != true) return;

        // The new part is on disk but not in the index, so bring it in and wear it right
        // away - saving a sprite and then hunting for it would be a poor reward.
        var id = editor.SaveId.Trim();
        var path = PccPartWriter.PathFor(_app.Paths, part.Set, part.Layer, id);

        _library.Add(part.Layer, part.Set, id, path, "Made here", isMine: true);

        SelectSlot(slot);
        Choose(Parts.Concat(Creations).FirstOrDefault(p => !p.IsNone && p.Id == id));

        StatusMessage = $"Saved {System.IO.Path.GetFileName(path)} and put it on. "
                        + "It is in your override package, so the game will load it too.";
    }

    /// <summary>
    /// Stars a part, which moves it to the front of its list.
    ///
    /// The list is rebuilt rather than the tile moved, because favourites sort ahead of
    /// everything and doing that by hand in an observable collection is how ordering
    /// bugs get in.
    /// </summary>
    private void ToggleFavourite(PccPartViewModel? part)
    {
        if (part is null or { IsNone: true }) return;

        var key = PccLibrary.KeyOf(part.Layer, part.Part.Set, part.Id);
        var favourites = _app.Settings.FavouriteParts;

        if (part.IsFavourite) favourites.RemoveAll(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
        else if (!favourites.Contains(key, StringComparer.OrdinalIgnoreCase)) favourites.Add(key);

        _app.SaveSettings();

        if (SelectedSlot is { } slot) SelectSlot(slot);
    }

    /// <summary>
    /// Deletes a part made here. Only ever one made here - a mod's file belongs to the
    /// mod, and removing it would be undone by Steam and blamed on the mod.
    /// </summary>
    private void DeletePart(PccPartViewModel? part)
    {
        if (part is null or { IsMine: false } || _app.Paths is null) return;

        try
        {
            System.IO.File.Delete(part.Part.FullPath);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not delete it: " + ex.Message;
            return;
        }

        _library.Remove(part.Part);

        var key = PccLibrary.KeyOf(part.Layer, part.Part.Set, part.Id);
        _app.Settings.FavouriteParts.RemoveAll(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
        _app.SaveSettings();

        // If the character was wearing it, it is not wearing it any more.
        if (string.Equals(_style.Get(part.Layer)?.FileId, part.Id, StringComparison.OrdinalIgnoreCase))
            _style.Set(part.Layer, null);

        if (SelectedSlot is { } slot) { RefreshSlot(slot); SelectSlot(slot); }

        StatusMessage = $"Deleted {part.Id}.";
        Render();
    }

    private void RefreshSavedDyes()
    {
        SavedDyes.Clear();

        foreach (var hex in _app.Settings.SavedColours)
            SavedDyes.Add(new DyeViewModel(hex) { CanForget = true });

        OnPropertyChanged(nameof(HasSavedDyes));
    }

    /// <summary>Rolls a different part for the slot being edited, keeping its colour.</summary>
    private void RandomSlotPart()
    {
        var slot = SelectedSlot;
        if (slot is null || Parts.Count == 0) return;

        Choose(Parts[_random.Next(Parts.Count)]);
    }

    private void RefreshSlot(DressUpSlotViewModel slot)
    {
        var choice = _style.Get(slot.Layer);
        var mod = choice is null ? null : _library.Resolve(slot.Layer, choice)?.ModName;

        slot.Show(choice?.Id, mod, choice?.Colour);
    }

    private void RefreshAllSlots()
    {
        foreach (var slot in Slots) RefreshSlot(slot);
        if (SelectedSlot is not null) SelectSlot(SelectedSlot);
    }

    // ---- characters ----

    /// <summary>Starts a character that is already a character: a body and nothing else.</summary>
    private void NewCharacter()
    {
        _style = new PccStyle();

        if (_library.DefaultBody() is { } body)
        {
            _style.Set(PccSlots.BodyLayer, new PccChoice
            {
                Set = body.Set, Id = body.Id, Colour = DefaultSkin(),
            });
        }

        CharacterName = "New character";
        StatusMessage = null;

        RefreshAllSlots();
        Render();
    }

    /// <summary>
    /// A skin tone for a new character.
    ///
    /// Taken from the bodies the user has already saved, because that is a better guess
    /// than anything invented here. Undyed a body is drawn in its raw greyscale, which
    /// on screen is a white figure with no skin at all.
    /// </summary>
    private string DefaultSkin()
    {
        var used = SavedStyles
            .Select(s => s.Style.Get(PccSlots.BodyLayer)?.Colour)
            .Where(c => c is not null)
            .GroupBy(c => c!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return used?.Key ?? "92847B";
    }

    private void Randomise()
    {
        _style = new PccStyle();

        foreach (var slot in PccSlots.All)
        {
            var parts = _library.InLayer(slot.Layer).Where(p => IsWearable(p.Set)).ToList();
            if (parts.Count == 0) continue;

            // A character always has a body, a head and a face; it does not always have
            // a cape or gloves.
            var essential = slot.Layer is "body" or "head" or "hair" or "face" or "eye";
            if (!essential && _random.NextDouble() < 0.55) continue;

            var part = parts[_random.Next(parts.Count)];
            var dye = Palette[_random.Next(Palette.Length)];

            _style.Set(slot.Layer, new PccChoice { Set = part.Set, Id = part.Id, Colour = dye });
        }

        CharacterName = "Random character";
        RefreshAllSlots();
        Render();
    }

    private void Load(SavedStyleViewModel? saved)
    {
        if (saved is null) return;

        _style = saved.Style.Copy();
        CharacterName = saved.Name;

        var (_, missing) = _library.ResolveStyle(_style);

        StatusMessage = missing.Count == 0
            ? $"Loaded {saved.Name}."
            : $"Loaded {saved.Name}, but {missing.Count} of its parts are not installed here: "
              + string.Join(", ", missing.Take(4))
              + ". They are kept in the character and will come back if you install the mod.";

        RefreshAllSlots();
        Render();
    }

    private void Save()
    {
        if (_app.Paths is null) return;

        try
        {
            var safe = string.Join("_", CharacterName.Trim()
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            if (safe.Length == 0) { StatusMessage = "That name cannot be a file name."; return; }

            var path = PccStyleFile.Save(_app.Paths.ElinRoot, _style, safe);

            StatusMessage = $"Saved to {path}. In Elin, open Edit Appearance and press Import.";
            LoadSavedStyles();
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not save: " + ex.Message;
        }
    }

    /// <summary>
    /// Writes one of the five slots the game's Register buttons use, so the character
    /// turns up behind Load Fav rather than needing Import at all.
    /// </summary>
    private void SaveToFavourite(string? slot)
    {
        if (_app.Paths is null || !int.TryParse(slot, out var index)) return;
        if (index is < 1 or > PccStyleFile.FavouriteSlots) return;

        try
        {
            // The buttons are one-based; the files are zero-based.
            PccStyleFile.Save(_app.Paths.ElinRoot, _style, $"fav{index - 1}");

            StatusMessage = $"Written to Fav{index}. In Elin, open Edit Appearance and "
                            + $"press Load Fav{index}.";
            LoadSavedStyles();
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not save: " + ex.Message;
        }
    }

    private void OpenFolder()
    {
        if (_app.Paths is null) return;
        ShellService.OpenFolder(PccStyleFile.FolderIn(_app.Paths.ElinRoot));
    }

    private async void Render() => Preview = await ComposeAsync(Direction, Frame, scale: 6);

    /// <summary>
    /// Draws the character at a given facing and frame. Public so the sprite editor can
    /// keep its backdrop pointing the same way as the cell being edited.
    /// </summary>
    public Task<BitmapSource?> ComposeAsync(int direction, int frame, int scale,
        IReadOnlySet<string>? hidden = null)
    {
        var (found, _) = _library.ResolveStyle(_style);

        var wanted = found
            .Where(f => hidden is null || !hidden.Contains(f.Layer))
            .Select(f => (f.Layer, f.Part.FullPath, f.Choice.Rgb()))
            .ToList();

        return Task.Run(() =>
        {
            var pieces = new List<PccPiece>();

            foreach (var (layer, path, tint) in wanted)
            {
                var sheet = PixelDecoder.Decode(path);
                if (sheet is null || !PccComposer.LooksLikeSheet(sheet)) continue;

                pieces.Add(new PccPiece { Layer = layer, Sheet = sheet, Tint = tint });
            }

            return PixelDecoder.ToBitmap(PccComposer.Compose(pieces, direction, frame, scale));
        });
    }
}
