using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.ViewModels;

/// <summary>What a click on the canvas does.</summary>
public enum SpriteTool
{
    Pencil,
    Eraser,
    Fill,
    Dropper,
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
    private readonly List<byte[]> _undo = new();
    private readonly List<byte[]> _redo = new();

    private PccSheet _sheet;
    private BitmapSource? _canvas;
    private BitmapSource? _character;

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
        BitmapSource? character, Func<string, bool> nameTaken)
    {
        _app = app;
        Source = source;
        _sheet = sheet;
        _character = character;

        SaveId = PccPartName.Available(source.Id, nameTaken);

        foreach (var grey in new[] { 255, 224, 192, 160, 128, 96, 64, 32, 0 })
            Greys.Add(new DyeViewModel($"{grey:X2}{grey:X2}{grey:X2}"));

        foreach (var hex in _app.Settings.SavedColours) Saved.Add(new DyeViewModel(hex));

        PickToolCommand = new RelayCommand(p =>
        {
            if (Enum.TryParse<SpriteTool>(p as string, out var t)) Tool = t;
        });
        UndoCommand = new RelayCommand(_ => Undo(), _ => CanUndo);
        RedoCommand = new RelayCommand(_ => Redo(), _ => CanRedo);
        MirrorCommand = new RelayCommand(_ => Edit(s => s.MirrorCell(Direction, Frame)));
        ClearCommand = new RelayCommand(_ => Edit(s => s.ClearCell(Direction, Frame)));
        NudgeCommand = new RelayCommand(p => Nudge(p as string));
        CopyFromCommand = new RelayCommand(p => CopyFrom(p as string));
        ChooseColourCommand = new RelayCommand(p =>
        {
            if (p is DyeViewModel { Hex: { } hex }) Colour = hex;
        });
        PickFromScreenCommand = new AsyncRelayCommand(PickFromScreen);
        ZoomCommand = new RelayCommand(p =>
        {
            if (int.TryParse(p as string, out var by)) Zoom = Math.Clamp(Zoom + by, 4, 20);
        });

        Redraw();
    }

    public PccFile Source { get; }

    public ObservableCollection<DyeViewModel> Greys { get; } = new();
    public ObservableCollection<DyeViewModel> Saved { get; } = new();

    public RelayCommand PickToolCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand MirrorCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand NudgeCommand { get; }
    public RelayCommand CopyFromCommand { get; }
    public RelayCommand ChooseColourCommand { get; }
    public AsyncRelayCommand PickFromScreenCommand { get; }
    public RelayCommand ZoomCommand { get; }


    /// <summary>Called with the new part's path once it has been written.</summary>
    public Action<string>? Finished { get; set; }

    public SpriteTool Tool
    {
        get => _tool;
        set { SetProperty(ref _tool, value); RaiseToolFlags(); }
    }

    public bool IsPencil => _tool == SpriteTool.Pencil;
    public bool IsEraser => _tool == SpriteTool.Eraser;
    public bool IsFill => _tool == SpriteTool.Fill;
    public bool IsDropper => _tool == SpriteTool.Dropper;

    private void RaiseToolFlags()
    {
        OnPropertyChanged(nameof(IsPencil));
        OnPropertyChanged(nameof(IsEraser));
        OnPropertyChanged(nameof(IsFill));
        OnPropertyChanged(nameof(IsDropper));
    }

    public int Nib
    {
        get => _nib;
        set => SetProperty(ref _nib, Math.Clamp(value, 1, 4));
    }

    /// <summary>Which facing is being drawn. Every cell is edited separately.</summary>
    public int Direction
    {
        get => _direction;
        set
        {
            SetProperty(ref _direction, Math.Clamp(value, 0, PccComposer.Rows - 1));
            OnPropertyChanged(nameof(CellName));
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
            Redraw();
        }
    }

    public string CellName => $"{PccFacing.NameOf(Direction)}, frame {Frame + 1} of {PccComposer.Columns}";

    public int Zoom
    {
        get => _zoom;
        set
        {
            SetProperty(ref _zoom, Math.Clamp(value, 4, 20));
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
        set { SetProperty(ref _colour, value); OnPropertyChanged(nameof(ColourBrush)); }
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
    public void Apply(int x, int y, bool starting)
    {
        if (Tool == SpriteTool.Dropper)
        {
            var pixel = _sheet.Get(Direction, Frame, x, y);
            if (pixel.A > 0) Colour = PccColour.ToHex(pixel.R, pixel.G, pixel.B);
            return;
        }

        if (starting) Remember();

        switch (Tool)
        {
            case SpriteTool.Pencil when PccColour.FromHex(Colour) is { } rgb:
                _sheet.Draw(Direction, Frame, x, y, Nib, rgb.B, rgb.G, rgb.R, 255);
                break;

            case SpriteTool.Eraser:
                _sheet.Draw(Direction, Frame, x, y, Nib, 0, 0, 0, 0);
                break;

            case SpriteTool.Fill when PccColour.FromHex(Colour) is { } fill:
                _sheet.Fill(Direction, Frame, x, y, fill.B, fill.G, fill.R, 255);
                break;
        }

        Redraw();
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
        _undo.Add(_sheet.Snapshot());
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);

        _redo.Clear();
        RaiseHistory();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;

        _redo.Add(_sheet.Snapshot());
        _sheet.Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);

        RaiseHistory();
        Redraw();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;

        _undo.Add(_sheet.Snapshot());
        _sheet.Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);

        RaiseHistory();
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

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = _sheet.Get(Direction, Frame, x / zoom, y / zoom);
            var i = (y * width + x) * 4;

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
            var path = PccPartWriter.Save(_app.Paths, _sheet, Source.Layer, SaveId);

            Status = $"Saved as {System.IO.Path.GetFileName(path)}.";
            Finished?.Invoke(path);
        }
        catch (Exception ex)
        {
            Status = "Could not save: " + ex.Message;
        }
    }
}
