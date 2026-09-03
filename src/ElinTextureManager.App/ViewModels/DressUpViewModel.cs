using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One installed PCC file, offered for a layer.</summary>
public sealed class PccPartViewModel : ObservableObject
{
    private BitmapSource? _thumbnail;
    private bool _requested;
    private bool _isChosen;

    public PccPartViewModel(TextureFile file, string layer)
    {
        File = file;
        Layer = layer;
    }

    public TextureFile File { get; }
    public string Layer { get; }

    public string FileName => File.FileName;
    public string ModName => File.ModName;
    public string? UniqueId => PccLayer.UniqueIdOf(File.FileName);

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }

    /// <summary>
    /// The part's own front-facing cell rather than its whole sheet.
    ///
    /// A raw sheet is sixteen small poses and reads as noise at thumbnail size; one cell
    /// at four times the size is the difference between browsing and squinting.
    /// </summary>
    public BitmapSource? Thumbnail
    {
        get { Ensure(); return _thumbnail; }
        private set => SetProperty(ref _thumbnail, value);
    }

    private async void Ensure()
    {
        if (_requested) return;
        _requested = true;

        var path = File.FullPath;
        var layer = Layer;

        Thumbnail = await Task.Run(() =>
        {
            var sheet = PixelDecoder.Decode(path);
            if (sheet is null || !PccComposer.LooksLikeSheet(sheet)) return null;

            var piece = new PccPiece { Layer = layer, Sheet = sheet };
            return PixelDecoder.ToBitmap(PccComposer.Compose(new[] { piece }, 0, 0, scale: 3));
        });
    }
}

/// <summary>A layer in the stack, with whatever has been chosen for it.</summary>
public sealed class DressUpLayerViewModel : ObservableObject
{
    private PccPartViewModel? _chosen;

    public DressUpLayerViewModel(string layer, int available)
    {
        Layer = layer;
        Available = available;
    }

    public string Layer { get; }
    public int Available { get; }

    public string Title => char.ToUpperInvariant(Layer[0]) + Layer[1..];

    public PccPartViewModel? Chosen
    {
        get => _chosen;
        set
        {
            SetProperty(ref _chosen, value);
            OnPropertyChanged(nameof(ChosenText));
            OnPropertyChanged(nameof(HasChoice));
        }
    }

    public bool HasChoice => _chosen is not null;

    public string ChosenText => _chosen is null
        ? $"{Available} available"
        : $"{_chosen.UniqueId}  ·  {_chosen.ModName}";
}

/// <summary>
/// The Dress Up page: builds a character out of the PCC parts installed across every mod.
///
/// The library holds nearly three thousand of these. They are greyscale sheets of
/// sixteen tiny poses that Elin tints as it draws, so one on its own tells you almost
/// nothing - which is also why the picture search cannot identify them. Stacking them in
/// the order the game does is what makes them legible, and it answers the question
/// people actually have: what does this hair look like on a character, and which mod
/// was it from.
/// </summary>
public sealed class DressUpViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Dictionary<string, List<PccPartViewModel>> _byLayer = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _animation;
    private readonly Random _random = new();

    private DressUpLayerViewModel? _selectedLayer;
    private BitmapSource? _preview;
    private int _frame;
    private int _direction;
    private bool _animate = true;

    public DressUpViewModel(AppServices app)
    {
        _app = app;

        SelectLayerCommand = new RelayCommand(p => SelectLayer(p as DressUpLayerViewModel));
        ChoosePartCommand = new RelayCommand(p => ChoosePart(p as PccPartViewModel));
        ClearLayerCommand = new RelayCommand(_ => ChoosePart(null));
        RandomiseCommand = new RelayCommand(_ => Randomise());
        ClearAllCommand = new RelayCommand(_ => ClearAll());
        FaceCommand = new RelayCommand(p =>
        {
            if (int.TryParse(p as string, out var d)) Direction = d;
        });

        // Four frames at roughly walking pace. The parts are animation sheets; showing
        // one still frame of a walk cycle hides half of what a part actually looks like.
        _animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _animation.Tick += (_, _) => { if (_animate) Frame = (Frame + 1) % PccComposer.Columns; };
    }

    public ObservableCollection<DressUpLayerViewModel> Layers { get; } = new();

    /// <summary>Parts offered for the layer currently being edited.</summary>
    public ObservableCollection<PccPartViewModel> Parts { get; } = new();

    public RelayCommand SelectLayerCommand { get; }
    public RelayCommand ChoosePartCommand { get; }
    public RelayCommand ClearLayerCommand { get; }
    public RelayCommand RandomiseCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand FaceCommand { get; }

    public DressUpLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        private set { SetProperty(ref _selectedLayer, value); OnPropertyChanged(nameof(PartsTitle)); }
    }

    public string PartsTitle => _selectedLayer is null
        ? "PICK A LAYER"
        : $"{_selectedLayer.Title.ToUpperInvariant()}  -  {Parts.Count} PARTS";

    public BitmapSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public int Frame
    {
        get => _frame;
        private set { SetProperty(ref _frame, value); Render(); }
    }

    public int Direction
    {
        get => _direction;
        set { SetProperty(ref _direction, value); Render(); }
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

    public int TotalParts { get; private set; }

    public string ScopeText => $"{TotalParts:N0} parts from {_byLayer.Count} layers";

    public bool HasParts => TotalParts > 0;

    /// <summary>Gathers every PCC file in the library, grouped by the layer it draws.</summary>
    public void Apply()
    {
        if (_byLayer.Count > 0) { _animation.Start(); return; }

        foreach (var file in _app.Scan.Index.Values
                     .SelectMany(e => e.Versions)
                     .Where(v => v.Kind == ReplacementKind.Pcc)
                     .GroupBy(v => v.FullPath, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            var layer = PccLayer.Of(file.FileName);
            if (layer is null) continue;

            if (!_byLayer.TryGetValue(layer, out var list)) _byLayer[layer] = list = new();
            list.Add(new PccPartViewModel(file, layer));
        }

        foreach (var list in _byLayer.Values)
            list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

        TotalParts = _byLayer.Values.Sum(l => l.Count);

        Layers.Clear();
        foreach (var layer in PccLayer.DrawOrder)
        {
            var count = _byLayer.TryGetValue(layer, out var list) ? list.Count : 0;
            if (count > 0) Layers.Add(new DressUpLayerViewModel(layer, count));
        }

        OnPropertyChanged(nameof(TotalParts));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(HasParts));

        if (Layers.Count > 0) Randomise();
        SelectLayer(Layers.FirstOrDefault(l => l.Layer == "hair") ?? Layers.FirstOrDefault());

        _animation.Start();
    }

    /// <summary>Stops the animation when the page is left, so it costs nothing in the background.</summary>
    public void Suspend() => _animation.Stop();

    private void SelectLayer(DressUpLayerViewModel? layer)
    {
        if (layer is null) return;

        SelectedLayer = layer;
        Parts.Clear();

        if (_byLayer.TryGetValue(layer.Layer, out var list))
            foreach (var part in list) Parts.Add(part);

        OnPropertyChanged(nameof(PartsTitle));
    }

    private void ChoosePart(PccPartViewModel? part)
    {
        var layer = part is null
            ? SelectedLayer
            : Layers.FirstOrDefault(l => l.Layer == part.Layer);

        if (layer is null) return;

        if (layer.Chosen is { } previous) previous.IsChosen = false;
        layer.Chosen = part;
        if (part is not null) part.IsChosen = true;

        Render();
    }

    private void Randomise()
    {
        foreach (var layer in Layers)
        {
            if (!_byLayer.TryGetValue(layer.Layer, out var list) || list.Count == 0) continue;

            // A character wears a body and a face; it does not always wear a cape.
            var essential = layer.Layer is "body" or "head" or "hair" or "face" or "eye";
            if (!essential && _random.NextDouble() < 0.5)
            {
                if (layer.Chosen is { } none) none.IsChosen = false;
                layer.Chosen = null;
                continue;
            }

            if (layer.Chosen is { } old) old.IsChosen = false;

            var pick = list[_random.Next(list.Count)];
            layer.Chosen = pick;
            pick.IsChosen = true;
        }

        Render();
    }

    private void ClearAll()
    {
        foreach (var layer in Layers)
        {
            if (layer.Chosen is { } old) old.IsChosen = false;
            layer.Chosen = null;
        }

        Render();
    }

    private async void Render()
    {
        var chosen = Layers.Where(l => l.Chosen is not null)
            .Select(l => (l.Layer, l.Chosen!.File.FullPath))
            .ToList();

        var direction = Direction;
        var frame = Frame;

        Preview = await Task.Run(() =>
        {
            var pieces = new List<PccPiece>();

            foreach (var (layer, path) in chosen)
            {
                var sheet = PixelDecoder.Decode(path);
                if (sheet is null || !PccComposer.LooksLikeSheet(sheet)) continue;

                pieces.Add(new PccPiece { Layer = layer, Sheet = sheet });
            }

            return PixelDecoder.ToBitmap(PccComposer.Compose(pieces, direction, frame, scale: 6));
        });
    }
}
