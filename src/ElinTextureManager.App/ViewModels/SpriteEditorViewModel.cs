using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.ViewModels;

/// <summary>
/// One piece the character behind is wearing, and whether it is shown.
///
/// A hat drawn over the hair being edited is worse than no reference at all, so every
/// piece can be taken off independently.
/// </summary>
public sealed class BackdropPieceViewModel : ObservableObject
{
    private bool _visible;

    public BackdropPieceViewModel(string layer, string label, bool visible)
    {
        Layer = layer;
        Label = label;
        _visible = visible;
    }

    public string Layer { get; }
    public string Label { get; }

    public bool Visible
    {
        get => _visible;
        set { if (SetProperty(ref _visible, value)) Changed?.Invoke(); }
    }

    /// <summary>Sets it without announcing, for changing several at once.</summary>
    public void SetQuietly(bool visible)
    {
        _visible = visible;
        OnPropertyChanged(nameof(Visible));
    }

    public event Action? Changed;
}

/// <summary>What a click on the canvas does.</summary>
public enum SpriteTool
{
    Pencil,
    Eraser,
    Fill,
    Dropper,
    Line,
    Rectangle,
    Ellipse,
    Star,
    ReplaceColour,
}

/// <summary>
/// A small editor for one PCC part.
///
/// Deliberately not a general pixel editor. A part is sixteen cells of 32x48 drawn in
/// greys so the game can dye it, worn by a character built on the page behind - so the
/// editor is cell-aware, offers a grey ramp before anything else, and draws the actual
/// character underneath. Layers, filters, shapes and the rest of what a drawing package
/// has would be weight without use at this size.
/// </summary>
public sealed class SpriteEditorViewModel : ObservableObject
{
    /// <summary>
    /// How many steps back can be taken. Each is a copy of the sheet, about 100 KB, so
    /// this is a few megabytes at worst - cheap next to losing an afternoon's drawing.
    /// </summary>
    private const int UndoDepth = 60;

    private readonly AppServices _app;
    // Tagged, because the two steps undo different things: the painting and the
    // marking. An untagged stack would try to restore a mask over a sheet, whose
    // lengths differ, and quietly do nothing at all.
    private readonly List<(bool Mask, byte[] Data)> _undo = new();
    private readonly List<(bool Mask, byte[] Data)> _redo = new();

    private readonly PccDyeMask _mask;
    private readonly Func<int, int, IReadOnlySet<string>, Task<BitmapSource?>>? _renderCharacter;

    private PccSheet _sheet;
    private BitmapSource? _canvas;
    private BitmapSource? _character;
    private bool _marking;
    private bool _maskStarted;
    private byte[]? _shapeBase;
    private (int X, int Y) _shapeFrom;
    private (int X, int Y)? _lastPoint;
    private bool _fillShapes;
    private bool _lineFromLast;
    private int _starPoints = 5;

    /// <summary>Set from Shift, which joins a stroke to where the last one ended.</summary>
    public bool LineFromLast
    {
        get => _lineFromLast;
        set => _lineFromLast = value;
    }

    /// <summary>How many points the star has.</summary>
    public int StarPoints
    {
        get => _starPoints;
        set => SetProperty(ref _starPoints, Math.Clamp(value, 3, 12));
    }

    /// <summary>Whether a shape comes out solid or as an outline.</summary>
    public bool FillShapes
    {
        get => _fillShapes;
        set => SetProperty(ref _fillShapes, value);
    }

    private SpriteTool _tool = SpriteTool.Pencil;
    private int _nib = 1;
    private int _direction;
    private int _frame;
    private int _zoom = 10;
    private double _characterOpacity = 0.45;
    private bool _showGrid = true;
    private bool _showCharacter = true;
    private string _colour = "808080";
    private string _saveId = string.Empty;
    private string? _status;

    public SpriteEditorViewModel(AppServices app, PccFile source, PccSheet sheet,
        BitmapSource? character, Func<string, bool> nameTaken,
        Func<int, int, IReadOnlySet<string>, Task<BitmapSource?>>? renderCharacter = null,
        IReadOnlyList<(string Layer, string Label)>? worn = null,
        string? suggestedName = null)
    {
        _app = app;
        Source = source;
        _sheet = sheet;
        _character = character;
        _mask = new PccDyeMask(sheet.Width, sheet.Height);
        _renderCharacter = renderCharacter;

        // A blank sprite has no source to copy a name from, so it gets one from its slot.
        IsNew = string.IsNullOrEmpty(source.Id);
        SaveId = PccPartName.Available(suggestedName ?? source.Id, nameTaken,
            suffix: IsNew ? string.Empty : "copy");

        foreach (var (layer, label) in worn ?? Array.Empty<(string, string)>())
        {
            // The layer being edited starts hidden. The canvas already draws the version
            // being worked on; leaving the old one underneath means drawing over a near
            // copy of itself and wondering which lines are yours.
            var piece = new BackdropPieceViewModel(layer, label,
                visible: !string.Equals(layer, source.Layer, StringComparison.OrdinalIgnoreCase));

            piece.Changed += RefreshCharacter;
            Behind.Add(piece);
        }

        // Paint in colour: a part is easier to draw as it should look than as the grey
        // it will be stored as. Which of it follows the character's dye is decided after.
        foreach (var hex in new[]
                 {
                     "FFFFFF", "C8C8C8", "909090", "585858", "202020",
                     "D06060", "E08840", "E0C050", "70B060", "50A0B0",
                     "5878C0", "8060B0", "C070A0", "8A6A4A", "B89070",
                 })
            Greys.Add(new DyeViewModel(hex));

        foreach (var hex in _app.Settings.SavedColours) Saved.Add(new DyeViewModel(hex));

        PickToolCommand = new RelayCommand(p =>
        {
            if (Enum.TryParse<SpriteTool>(p as string, out var t)) Tool = t;
        });
        UndoCommand = new RelayCommand(_ => Undo(), _ => CanUndo);
        RedoCommand = new RelayCommand(_ => Redo(), _ => CanRedo);
        MirrorCommand = new RelayCommand(_ => Edit(s => s.MirrorCell(Direction, Frame)));

        SendToCommand = new RelayCommand(p => SendTo(p as string));
        ClearCommand = new RelayCommand(_ => Edit(s => s.ClearCell(Direction, Frame)));
        NudgeCommand = new RelayCommand(p => Nudge(p as string));
        CopyFromCommand = new RelayCommand(p => CopyFrom(p as string));
        ChooseColourCommand = new RelayCommand(p =>
        {
            if (p is DyeViewModel { Hex: { } hex }) Colour = hex;
        });
        PickFromScreenCommand = new AsyncRelayCommand(PickFromScreen);
        SaveColourCommand = new RelayCommand(_ => SaveColour());
        ZoomCommand = new RelayCommand(p =>
        {
            if (int.TryParse(p as string, out var by)) Zoom += by;
        });
        NextCommand = new RelayCommand(_ => GoToMarking());
        JustTheBodyCommand = new RelayCommand(_ => ShowOnlyBody());
        ShowEverythingCommand = new RelayCommand(_ => ShowEverything());
        BackCommand = new RelayCommand(_ => Marking = false);
        MarkAllCommand = new RelayCommand(_ =>
        {
            RememberMask();
            _mask.MarkAllDrawn(_sheet);
            Redraw();
        });
        MarkNoneCommand = new RelayCommand(_ =>
        {
            RememberMask();
            _mask.Clear();
            Redraw();
        });

        Redraw();

        // Draws the backdrop without the layer being edited, which the constructor's
        // ready-made picture still has in it.
        RefreshCharacter();
    }

    public PccFile Source { get; }

    /// <summary>Started from nothing rather than from an existing part.</summary>
    public bool IsNew { get; }

    /// <summary>
    /// What the window says it is doing. A new sprite has no original to reassure
    /// anyone about, and saying "editing a copy of" with nothing after it would be worse
    /// than saying nothing.
    /// </summary>
    public string Subtitle => IsNew
        ? $"A new {SlotLabel.ToLowerInvariant()}, from nothing. The character behind is "
          + "there to draw against."
        : $"Editing a copy of {Source.Id} from {Source.ModName} - the original is never touched.";

    public string SlotLabel => PccSlots.For(Source.Layer)?.Label ?? Source.Layer;

    public string Title => IsNew ? $"New {SlotLabel.ToLowerInvariant()} sprite" : "Edit sprite";

    /// <summary>
    /// The colours this sprite already uses, most-used first.
    ///
    /// The palette an artist most wants to hand: matching a shade by eye off a wheel is
    /// how a sixteen-colour sprite quietly becomes a forty-colour one.
    /// </summary>
    public IReadOnlyList<DyeViewModel> PaletteInUse => _sheet.ColoursUsed()
        .Select(c => new DyeViewModel(PccColour.ToHex(c.R, c.G, c.B)))
        .ToList();

    /// <summary>Each piece the character behind wears, with its own switch.</summary>
    public ObservableCollection<BackdropPieceViewModel> Behind { get; } = new();

    public bool HasBehind => Behind.Count > 0;

    public ObservableCollection<DyeViewModel> Greys { get; } = new();
    public ObservableCollection<DyeViewModel> Saved { get; } = new();

    public RelayCommand PickToolCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand MirrorCommand { get; }

    public RelayCommand SendToCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand NudgeCommand { get; }
    public RelayCommand CopyFromCommand { get; }
    public RelayCommand ChooseColourCommand { get; }
    public AsyncRelayCommand PickFromScreenCommand { get; }
    public RelayCommand SaveColourCommand { get; }
    public RelayCommand ZoomCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand JustTheBodyCommand { get; }
    public RelayCommand ShowEverythingCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand MarkAllCommand { get; }
    public RelayCommand MarkNoneCommand { get; }


    /// <summary>Called with the new part's path once it has been written.</summary>
    public Action<string>? Finished { get; set; }

    /// <summary>
    /// False while painting, true while choosing what the character's dye reaches.
    ///
    /// Two steps rather than one because they are two different jobs: drawing the thing
    /// as it should look, and then saying which of it is cloth that follows the wearer
    /// and which is a brass buckle that stays brass.
    /// </summary>
    public bool Marking
    {
        get => _marking;
        private set
        {
            SetProperty(ref _marking, value);
            OnPropertyChanged(nameof(Painting));
            OnPropertyChanged(nameof(StageTitle));
            OnPropertyChanged(nameof(StageHint));
            OnPropertyChanged(nameof(MarkedCount));
            Redraw();
        }
    }

    public bool Painting => !_marking;

    public string StageTitle => _marking ? "STEP 2 - WHAT TAKES THE DYE" : "STEP 1 - PAINT IT";

    /// <summary>
    /// Careful about what it promises. The game multiplies the whole part by one
    /// colour, so an unmarked pixel is not untouched - it keeps its own hue through the
    /// multiply rather than ignoring the dye. Saying "keeps the colour you painted"
    /// flatly would be a promise the format cannot keep.
    /// </summary>
    public string StageHint => _marking
        ? "Red is stored as grey, so the character's colour comes through it cleanly. "
          + "The rest keeps the hue you painted - the game tints the whole part, so it "
          + "is shaded by the dye rather than ignoring it. Paint to mark, erase to unmark."
        : "Paint it as it should look. You choose what the character's colour reaches "
          + "on the next step.";

    public string MarkedCount => _marking
        ? $"{_mask.CountIn(Direction, Frame)} pixels of this cell take the dye"
        : string.Empty;

    /// <summary>
    /// Moves to marking, starting from everything the part draws.
    ///
    /// That default because it is what every installed part does - the whole thing
    /// follows the character - so the common case needs no work and the exceptions are
    /// erased out of it.
    /// </summary>
    private void GoToMarking()
    {
        if (!_maskStarted)
        {
            _mask.MarkAllDrawn(_sheet);
            _maskStarted = true;
        }

        Tool = SpriteTool.Pencil;
        Marking = true;
    }

    public SpriteTool Tool
    {
        get => _tool;
        set { SetProperty(ref _tool, value); RaiseToolFlags(); }
    }

    public bool IsPencil => _tool == SpriteTool.Pencil;
    public bool IsEraser => _tool == SpriteTool.Eraser;
    public bool IsFill => _tool == SpriteTool.Fill;
    public bool IsDropper => _tool == SpriteTool.Dropper;
    public bool IsLine => _tool == SpriteTool.Line;
    public bool IsRectangle => _tool == SpriteTool.Rectangle;
    public bool IsReplace => _tool == SpriteTool.ReplaceColour;

    public bool IsEllipse => _tool == SpriteTool.Ellipse;
    public bool IsStar => _tool == SpriteTool.Star;

    /// <summary>True for tools that are dragged out and only land when released.</summary>
    private bool IsShape =>
        _tool is SpriteTool.Line or SpriteTool.Rectangle or SpriteTool.Ellipse or SpriteTool.Star;

    /// <summary>Whether the solid switch means anything for the tool in hand.</summary>
    public bool ShapeCanBeSolid =>
        _tool is SpriteTool.Rectangle or SpriteTool.Ellipse or SpriteTool.Star;

    private void RaiseToolFlags()
    {
        OnPropertyChanged(nameof(IsPencil));
        OnPropertyChanged(nameof(IsEraser));
        OnPropertyChanged(nameof(IsFill));
        OnPropertyChanged(nameof(IsDropper));
        OnPropertyChanged(nameof(IsLine));
        OnPropertyChanged(nameof(IsRectangle));
        OnPropertyChanged(nameof(IsReplace));
        OnPropertyChanged(nameof(IsEllipse));
        OnPropertyChanged(nameof(IsStar));
        OnPropertyChanged(nameof(ShapeCanBeSolid));
        OnPropertyChanged(nameof(ToolName));
    }

    public string ToolName => _tool switch
    {
        SpriteTool.Eraser => "Eraser",
        SpriteTool.Fill => "Fill",
        SpriteTool.Dropper => "Pick colour",
        SpriteTool.Line => "Line",
        SpriteTool.Rectangle => "Rectangle",
        SpriteTool.Ellipse => "Circle",
        SpriteTool.Star => "Star",
        SpriteTool.ReplaceColour => "Replace colour",
        _ => "Pencil",
    };

    /// <summary>
    /// Brush size in pixels, 1 to 8. Bracket keys step it, as everywhere else.
    /// </summary>
    public int BrushSize
    {
        get => _nib;
        set { SetProperty(ref _nib, Math.Clamp(value, 1, 8)); OnPropertyChanged(nameof(BrushLabel)); }
    }

    public string BrushLabel => $"{BrushSize} px";

    /// <summary>Which facing is being drawn. Every cell is edited separately.</summary>
    public int Direction
    {
        get => _direction;
        set
        {
            SetProperty(ref _direction, Math.Clamp(value, 0, PccComposer.Rows - 1));
            OnPropertyChanged(nameof(CellName));
            OnPropertyChanged(nameof(MarkedCount));
            RefreshCharacter();
            Redraw();
        }
    }

    public int Frame
    {
        get => _frame;
        set
        {
            SetProperty(ref _frame, Math.Clamp(value, 0, PccComposer.Columns - 1));
            OnPropertyChanged(nameof(CellName));
            OnPropertyChanged(nameof(MarkedCount));
            RefreshCharacter();
            Redraw();
        }
    }

    /// <summary>
    /// Redraws the character behind to face the way the cell being edited does.
    ///
    /// Without this the backdrop stays facing front while the back of a hat is drawn
    /// against it, which is worse than no backdrop at all - it is a reference that
    /// quietly disagrees with the work.
    /// </summary>
    private async void RefreshCharacter()
    {
        if (_renderCharacter is null) return;

        var hidden = Behind.Where(p => !p.Visible).Select(p => p.Layer)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rendered = await _renderCharacter(Direction, Frame, hidden);

        _character = rendered;
        OnPropertyChanged(nameof(Character));
    }

    /// <summary>Strips the character back to its body, for when everything is in the way.</summary>
    private void ShowOnlyBody()
    {
        foreach (var piece in Behind)
            piece.SetQuietly(string.Equals(piece.Layer, PccSlots.BodyLayer,
                StringComparison.OrdinalIgnoreCase));

        RefreshCharacter();
    }

    private void ShowEverything()
    {
        foreach (var piece in Behind) piece.SetQuietly(true);
        RefreshCharacter();
    }

    public string CellName => $"{PccFacing.NameOf(Direction)}, frame {Frame + 1} of {PccComposer.Columns}";

    /// <summary>
    /// Pixels across per sprite pixel, 2 to 64.
    ///
    /// The old ceiling of 20 was too low to place a single pixel confidently on a 32
    /// wide cell; at 64 one pixel is a comfortable target.
    /// </summary>
    public int Zoom
    {
        get => _zoom;
        set
        {
            SetProperty(ref _zoom, Math.Clamp(value, 2, 64));
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            Redraw();
        }
    }

    public double CanvasWidth => _sheet.CellWidth * Zoom;
    public double CanvasHeight => _sheet.CellHeight * Zoom;

    public bool ShowGrid
    {
        get => _showGrid;
        set { SetProperty(ref _showGrid, value); Redraw(); }
    }

    public bool ShowCharacter
    {
        get => _showCharacter;
        set { SetProperty(ref _showCharacter, value); OnPropertyChanged(nameof(CharacterVisible)); }
    }

    public double CharacterOpacity
    {
        get => _characterOpacity;
        set { SetProperty(ref _characterOpacity, value); OnPropertyChanged(nameof(CharacterVisible)); }
    }

    /// <summary>The character being built, drawn behind so the part is seen in place.</summary>
    public BitmapSource? Character => _character;

    public double CharacterVisible => _showCharacter ? _characterOpacity : 0;

    public BitmapSource? Canvas
    {
        get => _canvas;
        private set => SetProperty(ref _canvas, value);
    }

    public string Colour
    {
        get => _colour;
        set
        {
            // Typed in with a hash, as anyone writing a hex colour would.
            var cleaned = value?.TrimStart('#').Trim() ?? string.Empty;
            if (PccColour.FromHex(cleaned) is null) { OnPropertyChanged(nameof(HexText)); return; }

            SetProperty(ref _colour, cleaned.ToUpperInvariant());

            OnPropertyChanged(nameof(ColourBrush));
            OnPropertyChanged(nameof(HexText));
            OnPropertyChanged(nameof(Grey));
            Remember(_colour);
        }
    }

    /// <summary>The colour as it is written, with the hash people expect in front.</summary>
    public string HexText
    {
        get => "#" + _colour;
        set => Colour = value;
    }

    /// <summary>
    /// Straight black to white, because reaching for a grey on a colour wheel means
    /// dragging to the exact centre and the wheel has no exact centre.
    /// </summary>
    public double Grey
    {
        get => PccColour.FromHex(_colour) is { } rgb
               && rgb.R == rgb.G && rgb.G == rgb.B ? rgb.R : double.NaN;
        set
        {
            var level = (byte)Math.Clamp(value, 0, 255);
            Colour = PccColour.ToHex(level, level, level);
        }
    }

    /// <summary>
    /// The colours used lately, newest first.
    ///
    /// Kept because shading is a conversation between three or four tones, and going
    /// back to the one before last should not mean finding it on the wheel again.
    /// </summary>
    public ObservableCollection<DyeViewModel> History { get; } = new();

    private void Remember(string hex)
    {
        var existing = History.FirstOrDefault(d =>
            string.Equals(d.Hex, hex, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) History.Remove(existing);

        History.Insert(0, new DyeViewModel(hex));
        while (History.Count > 18) History.RemoveAt(History.Count - 1);
    }

    public Brush ColourBrush => PccColour.FromHex(_colour) is { } rgb
        ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
        : Brushes.Transparent;

    public string SaveId
    {
        get => _saveId;
        set { SetProperty(ref _saveId, value); OnPropertyChanged(nameof(SaveProblem)); OnPropertyChanged(nameof(CanSave)); }
    }

    public string? SaveProblem => PccPartName.Check(Source.Layer, SaveId).Problem;

    public bool CanSave => PccPartName.Check(Source.Layer, SaveId).Ok;

    public string? Status
    {
        get => _status;
        private set { SetProperty(ref _status, value); OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // ---- drawing ----

    /// <summary>
    /// Applies a stroke at a point in cell coordinates.
    ///
    /// <paramref name="starting"/> marks the first press of a drag, which is where the
    /// undo step is taken: one step per stroke, not one per pixel dragged over.
    /// </summary>
    /// <summary>
    /// A stroke on the canvas.
    ///
    /// <paramref name="starting"/> marks the first press of a drag, which is where the
    /// undo step is taken - one per stroke, not one per pixel dragged over.
    /// <paramref name="erasing"/> comes from the right button, which erases whatever
    /// tool is held; every pixel editor works that way and the hand expects it.
    /// <paramref name="picking"/> comes from Alt, the temporary eyedropper.
    /// </summary>
    public void Apply(int x, int y, bool starting, bool erasing = false, bool picking = false)
    {
        if (picking || (!Marking && Tool == SpriteTool.Dropper))
        {
            var picked = _sheet.Get(Direction, Frame, x, y);

            // Saying so beats picking nothing in silence, which reads as a broken tool
            // rather than as an empty pixel.
            if (picked.A == 0) Status = "Nothing painted there yet.";
            else { Colour = PccColour.ToHex(picked.R, picked.G, picked.B); Status = null; }

            return;
        }

        if (Marking) { Mark(x, y, starting, erasing); return; }

        if (starting)
        {
            Remember();

            // A shape is dragged out and redrawn from this snapshot on every move, so
            // that only the final one lands.
            _shapeFrom = (x, y);
            _shapeBase = IsShape ? _sheet.Snapshot() : null;
        }

        var colour = PccColour.FromHex(Colour);

        switch (Tool)
        {
            case SpriteTool.Pencil when colour is { } rgb && !erasing:
                // Shift joins to where the last stroke ended, as it does everywhere else.
                if (starting && LineFromLast && _lastPoint.HasValue)
                {
                    var from = _lastPoint.Value;
                    _sheet.DrawLine(Direction, Frame, from.X, from.Y, x, y,
                        BrushSize, rgb.B, rgb.G, rgb.R, 255);
                }
                else
                {
                    _sheet.Draw(Direction, Frame, x, y, BrushSize, rgb.B, rgb.G, rgb.R, 255);
                }

                _lastPoint = (x, y);
                break;

            case SpriteTool.Pencil:
            case SpriteTool.Eraser:
                _sheet.Draw(Direction, Frame, x, y, BrushSize, 0, 0, 0, 0);
                _lastPoint = (x, y);
                break;

            case SpriteTool.Fill when colour is { } fill && !erasing:
                _sheet.Fill(Direction, Frame, x, y, fill.B, fill.G, fill.R, 255);
                break;

            case SpriteTool.Fill:
                _sheet.Fill(Direction, Frame, x, y, 0, 0, 0, 0);
                break;

            case SpriteTool.ReplaceColour when colour is { } swap:
                var target = _sheet.Get(Direction, Frame, x, y);
                if (target.A > 0)
                {
                    if (erasing) _sheet.ReplaceColour(Direction, Frame, target, 0, 0, 0, 0);
                    else _sheet.ReplaceColour(Direction, Frame, target, swap.B, swap.G, swap.R, 255);
                }
                break;

            case SpriteTool.Line when _shapeBase is not null:
            case SpriteTool.Rectangle when _shapeBase is not null:
                _sheet.Restore(_shapeBase);
                DrawShape(x, y, erasing, colour);
                break;
        }

        Redraw();
    }

    private void DrawShape(int x, int y, bool erasing, (byte R, byte G, byte B)? colour)
    {
        var (b, g, r, a) = erasing || colour is null
            ? ((byte)0, (byte)0, (byte)0, (byte)0)
            : (colour.Value.B, colour.Value.G, colour.Value.R, (byte)255);

        var (fx, fy) = _shapeFrom;

        switch (Tool)
        {
            case SpriteTool.Line:
                _sheet.DrawLine(Direction, Frame, fx, fy, x, y, BrushSize, b, g, r, a);
                break;

            case SpriteTool.Ellipse:
                _sheet.DrawEllipse(Direction, Frame, fx, fy, x, y, BrushSize, FillShapes, b, g, r, a);
                break;

            // Dragged from the middle outwards, the way a star is placed rather than
            // boxed in: its size and its rotation both come from one gesture.
            case SpriteTool.Star:
                _sheet.DrawStar(Direction, Frame, fx, fy, x, y, StarPoints, BrushSize,
                    FillShapes, b, g, r, a);
                break;

            default:
                _sheet.DrawRectangle(Direction, Frame, fx, fy, x, y, BrushSize, FillShapes, b, g, r, a);
                break;
        }

        _lastPoint = (x, y);
    }

    /// <summary>Ends a drag, so a shape stops being redrawn from its snapshot.</summary>
    public void FinishStroke()
    {
        _shapeBase = null;
        OnPropertyChanged(nameof(PaletteInUse));
    }

    /// <summary>
    /// Marks or unmarks pixels for dyeing. The pencil marks, the eraser unmarks, and the
    /// fill marks a whole region of one colour - which is the quick way to say "all of
    /// the cloth, none of the buckle".
    /// </summary>
    private void Mark(int x, int y, bool starting, bool erasing = false)
    {
        if (starting) RememberMask();

        switch (Tool)
        {
            case SpriteTool.Eraser:
                _mask.Paint(Direction, Frame, x, y, BrushSize, false);
                break;

            case not SpriteTool.Fill when erasing:
                _mask.Paint(Direction, Frame, x, y, BrushSize, false);
                break;

            case SpriteTool.Fill when erasing:
                MarkRegion(x, y, false);
                break;

            case SpriteTool.Fill:
                MarkRegion(x, y, true);
                break;

            default:
                _mask.Paint(Direction, Frame, x, y, BrushSize, true);
                break;
        }

        OnPropertyChanged(nameof(MarkedCount));
        Redraw();
    }

    /// <summary>Marks everything joined to this pixel that was painted the same colour.</summary>
    private void MarkRegion(int x, int y, bool dyeable)
    {
        var target = _sheet.Get(Direction, Frame, x, y);
        if (target.A == 0) return;

        var queue = new Queue<(int X, int Y)>();
        var seen = new bool[_sheet.CellWidth * _sheet.CellHeight];

        queue.Enqueue((x, y));
        seen[y * _sheet.CellWidth + x] = true;

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (_sheet.Get(Direction, Frame, cx, cy) != target) continue;

            _mask.Set(Direction, Frame, cx, cy, dyeable);

            foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= _sheet.CellWidth || ny >= _sheet.CellHeight) continue;

                var key = ny * _sheet.CellWidth + nx;
                if (seen[key]) continue;

                seen[key] = true;
                queue.Enqueue((nx, ny));
            }
        }
    }

    /// <summary>
    /// Copies this cell out to the others.
    ///
    /// The usual case by a wide margin: a hat does not change between the four frames of
    /// a walk, so drawing it once and sending it across beats drawing it four times.
    /// </summary>
    private void SendTo(string? where)
    {
        var targets = where switch
        {
            "frames" => Enumerable.Range(0, PccComposer.Columns).Select(f => (Direction, Frame: f)),
            "views" => Enumerable.Range(0, PccComposer.Rows).Select(d => (Direction: d, Frame)),
            "all" => Enumerable.Range(0, PccComposer.Rows).SelectMany(d =>
                Enumerable.Range(0, PccComposer.Columns).Select(f => (Direction: d, Frame: f))),
            _ => null,
        };

        if (targets is null) return;

        var list = targets.Where(t => t.Direction != Direction || t.Frame != Frame).ToList();
        if (list.Count == 0) return;

        Edit(sheet =>
        {
            foreach (var (direction, frame) in list)
                sheet.CopyCell(Direction, Frame, direction, frame);
        });

        Status = $"Copied into {list.Count} other cells. Undo puts them back.";
    }

    private void RememberMask()
    {
        _undo.Add((true, _mask.Snapshot()));
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);

        _redo.Clear();
        RaiseHistory();
    }

    private void Edit(Action<PccSheet> change)
    {
        Remember();
        change(_sheet);
        Redraw();
    }

    private void Nudge(string? direction)
    {
        var (dx, dy) = direction switch
        {
            "left" => (-1, 0),
            "right" => (1, 0),
            "up" => (0, -1),
            "down" => (0, 1),
            _ => (0, 0),
        };

        if (dx != 0 || dy != 0) Edit(s => s.NudgeCell(Direction, Frame, dx, dy));
    }

    /// <summary>Copies another cell over this one, for reusing a pose.</summary>
    private void CopyFrom(string? which)
    {
        var (fromDirection, fromFrame) = which switch
        {
            "previousFrame" => (Direction, (Frame + PccComposer.Columns - 1) % PccComposer.Columns),
            "front" => (PccFacing.Front, Frame),
            _ => (-1, -1),
        };

        if (fromDirection < 0) return;
        if (fromDirection == Direction && fromFrame == Frame) return;

        Edit(s => s.CopyCell(fromDirection, fromFrame, Direction, Frame));
        Status = "Copied in. Undo puts it back.";
    }

    // ---- history ----

    private void Remember()
    {
        _undo.Add((false, _sheet.Snapshot()));
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);

        _redo.Clear();
        RaiseHistory();
    }

    private void Undo() => Step(_undo, _redo);

    private void Redo() => Step(_redo, _undo);

    /// <summary>
    /// Moves one step between the two stacks, putting the current state on the other.
    /// Each step knows whether it is a painting or a marking, so undoing one never
    /// tries to restore it over the other.
    /// </summary>
    private void Step(List<(bool Mask, byte[] Data)> from, List<(bool Mask, byte[] Data)> to)
    {
        if (from.Count == 0) return;

        var (isMask, data) = from[^1];
        from.RemoveAt(from.Count - 1);

        to.Add(isMask ? (true, _mask.Snapshot()) : (false, _sheet.Snapshot()));

        if (isMask) _mask.Restore(data);
        else _sheet.Restore(data);

        RaiseHistory();
        OnPropertyChanged(nameof(MarkedCount));
        Redraw();
    }

    private void RaiseHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    // ---- the canvas ----

    /// <summary>
    /// Draws the cell at working size, with the pixel grid over it.
    ///
    /// Scaled here rather than by the layout so the grid lines land exactly between
    /// pixels: letting the view stretch a 32x48 image would put the lines wherever the
    /// rounding fell.
    /// </summary>
    private void Redraw()
    {
        var zoom = Zoom;
        var cw = _sheet.CellWidth;
        var ch = _sheet.CellHeight;

        var width = cw * zoom;
        var height = ch * zoom;
        var pixels = new byte[width * height * 4];

        // While marking, the cell is shown drained to grey with the dyeable pixels in
        // red - the point being to see the choice, not the artwork.
        var source = Marking ? PccFlatten.Preview(_sheet, _mask, Direction, Frame) : null;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;

            (byte B, byte G, byte R, byte A) pixel;

            if (source is not null)
            {
                var j = (y / zoom * source.Width + x / zoom) * 4;
                pixel = (source.Bgra[j], source.Bgra[j + 1], source.Bgra[j + 2], source.Bgra[j + 3]);
            }
            else
            {
                pixel = _sheet.Get(Direction, Frame, x / zoom, y / zoom);
            }

            pixels[i] = pixel.B;
            pixels[i + 1] = pixel.G;
            pixels[i + 2] = pixel.R;
            pixels[i + 3] = pixel.A;

            if (!ShowGrid || zoom < 6) continue;
            if (x % zoom != 0 && y % zoom != 0) continue;

            // A faint line over whatever is there, so the grid reads on dark and light
            // artwork alike.
            pixels[i] = Mix(pixels[i], 128, pixel.A);
            pixels[i + 1] = Mix(pixels[i + 1], 128, pixel.A);
            pixels[i + 2] = Mix(pixels[i + 2], 128, pixel.A);
            pixels[i + 3] = Math.Max(pixel.A, (byte)40);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96,
            PixelFormats.Bgra32, null, pixels, width * 4);

        bitmap.Freeze();
        Canvas = bitmap;
    }

    private static byte Mix(byte channel, byte towards, byte weight) =>
        (byte)(channel + (towards - channel) * (weight > 0 ? 0.18 : 1.0));

    /// <summary>Keeps the current colour, in the same list the creator saves into.</summary>
    private void SaveColour()
    {
        var hex = _colour;

        if (_app.Settings.SavedColours.Any(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"#{hex} is already saved.";
            return;
        }

        _app.Settings.SavedColours.Add(hex);
        _app.SaveSettings();

        Saved.Clear();
        foreach (var saved in _app.Settings.SavedColours) Saved.Add(new DyeViewModel(saved));

        Status = $"Saved #{hex}.";
    }

    private async Task PickFromScreen()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is null) return;

        var picked = await ScreenColourPicker.PickAsync(owner);
        if (picked is not null) Colour = picked;
    }

    // ---- saving ----

    /// <summary>
    /// Writes the part into the application's own package, where the game loads parts
    /// from and the creator will see it on the next scan.
    /// </summary>
    public void Save()
    {
        if (_app.Paths is null) { Status = "Set the Elin folder in Settings first."; return; }

        var check = PccPartName.Check(Source.Layer, SaveId);
        if (!check.Ok) { Status = check.Problem; return; }

        try
        {
            // Marked pixels go to grey so the character's colour comes through them;
            // the rest keep what they were painted.
            var flattened = PccSheet.From(PccFlatten.Apply(_sheet, _mask)) ?? _sheet;

            var path = PccPartWriter.Save(_app.Paths, flattened, Source.Layer, SaveId);

            Status = $"Saved as {System.IO.Path.GetFileName(path)}.";
            Finished?.Invoke(path);
        }
        catch (Exception ex)
        {
            Status = "Could not save: " + ex.Message;
        }
    }
}
