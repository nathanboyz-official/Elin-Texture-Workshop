using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One candidate the lookup turned up.</summary>
public sealed class IdentifyResultViewModel : ObservableObject
{
    private BitmapSource? _thumbnail;
    private bool _thumbnailRequested;

    public IdentifyResultViewModel(SpriteMatch match, TextureFile file, TextureEntry? entry)
    {
        Match = match;
        File = file;
        Entry = entry;
    }

    /// <summary>
    /// Loaded when the binding first asks for it, the same way the browser's tiles do,
    /// so twenty-four decodes happen on display rather than all at once on search.
    /// </summary>
    public BitmapSource? Thumbnail
    {
        get { EnsureThumbnail(); return _thumbnail; }
        private set => SetProperty(ref _thumbnail, value);
    }

    private async void EnsureThumbnail()
    {
        if (_thumbnailRequested) return;
        _thumbnailRequested = true;

        Thumbnail = await TextureImageLoader.LoadAsync(File.FullPath, 160);
    }

    public SpriteMatch Match { get; }
    public TextureFile File { get; }
    public TextureEntry? Entry { get; }

    public string FullPath => File.FullPath;
    public string FileName => File.FileName;
    public string ModName => File.ModName;
    public string? WorkshopId => File.WorkshopId;
    public bool IsStrongMatch => Match.IsStrongMatch;
    public string ConfidenceLabel => Match.ConfidenceLabel;

    public string ScoreText => Entry is { SourceCount: > 1 }
        ? $"{ConfidenceLabel}  ·  {Entry.SourceCount} mods supply this"
        : ConfidenceLabel;
}

/// <summary>
/// The Identify page: paste a screenshot, get the mods that could have supplied what is
/// in it.
///
/// The honest description of what this does is "narrows seven thousand images to a
/// couple of dozen worth looking at". On a library like this one the right file is
/// usually first and is labelled as such, but the last judgement - is that the same
/// character - stays with the person, because a person makes it instantly and correctly
/// and no score does.
/// </summary>
public sealed class IdentifyViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action<TextureEntry> _openDetail;
    private readonly SpriteFinder _finder = new();

    private BitmapSource? _queryImage;
    private PixelBuffer? _query;
    private string? _querySource;
    private string? _statusMessage;
    private bool _isBusy;
    private double _indexProgress;
    private bool _hasSearched;

    public IdentifyViewModel(AppServices app, Action<TextureEntry> openDetail)
    {
        _app = app;
        _openDetail = openDetail;

        PasteCommand = new RelayCommand(_ => PasteFromClipboard());
        BrowseCommand = new RelayCommand(_ => Browse());
        ClearCommand = new RelayCommand(_ => Clear(), _ => HasQuery);
        SearchCommand = new AsyncRelayCommand(() => SearchAsync(), () => HasQuery && !IsBusy);
        OpenResultCommand = new RelayCommand(OpenResult);
    }

    public ObservableCollection<IdentifyResultViewModel> Results { get; } = new();

    public RelayCommand PasteCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand OpenResultCommand { get; }

    public BitmapSource? QueryImage
    {
        get => _queryImage;
        private set
        {
            SetProperty(ref _queryImage, value);
            OnPropertyChanged(nameof(HasQuery));
            OnPropertyChanged(nameof(NoQuery));
            SearchCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasQuery => _query is not null;

    public bool NoQuery => _query is null;

    public string? QuerySource
    {
        get => _querySource;
        private set { SetProperty(ref _querySource, value); OnPropertyChanged(nameof(HasQuerySource)); }
    }

    public bool HasQuerySource => !string.IsNullOrEmpty(_querySource);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetProperty(ref _isBusy, value);
            OnPropertyChanged(nameof(IsIdle));
            SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !_isBusy;

    /// <summary>0-1 while the first colour index is being built; that pass is the slow one.</summary>
    public double IndexProgress
    {
        get => _indexProgress;
        private set { SetProperty(ref _indexProgress, value); OnPropertyChanged(nameof(ShowIndexProgress)); }
    }

    public bool ShowIndexProgress => _isBusy && _indexProgress is > 0 and < 1;

    public bool HasResults => Results.Count > 0;

    public bool FoundNothing => _hasSearched && Results.Count == 0;

    /// <summary>Takes an image straight off the clipboard, which is where a screenshot lands.</summary>
    public void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                Accept(Clipboard.GetImage(), "pasted from the clipboard");
                return;
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0 && files[0] is { } path) { AcceptFile(path); return; }
            }

            StatusMessage = "There is no image on the clipboard. "
                            + "Take a screenshot, crop it to the character, and copy it.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not read the clipboard: " + ex.Message;
        }
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a screenshot or a cropped image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
        };

        if (dialog.ShowDialog() == true) AcceptFile(dialog.FileName);
    }

    /// <summary>Called by the view when a file is dropped onto the page.</summary>
    public void AcceptFile(string path)
    {
        if (!File.Exists(path))
        {
            StatusMessage = "That file is not there any more.";
            return;
        }

        var buffer = PixelDecoder.Decode(path);
        if (buffer is null)
        {
            StatusMessage = "That file is not an image this can read.";
            return;
        }

        _query = buffer;
        QueryImage = TextureImageLoader.Load(path, 0);
        QuerySource = Path.GetFileName(path);
        StatusMessage = null;
        Results.Clear();
        _hasSearched = false;
        RaiseResultProperties();
    }

    private void Accept(BitmapSource? image, string source)
    {
        if (image is null) return;

        var buffer = PixelDecoder.Convert(image);
        if (buffer is null)
        {
            StatusMessage = "That image could not be read.";
            return;
        }

        _query = buffer;
        QueryImage = image;
        QuerySource = source;
        StatusMessage = null;
        Results.Clear();
        _hasSearched = false;
        RaiseResultProperties();
    }

    private void Clear()
    {
        _query = null;
        QueryImage = null;
        QuerySource = null;
        StatusMessage = null;
        Results.Clear();
        _hasSearched = false;
        RaiseResultProperties();
    }

    /// <summary>How many candidates are shown. Enough to scan by eye, few enough to scan.</summary>
    private const int ResultCount = 24;

    public async Task SearchAsync()
    {
        if (_query is not { } query) return;

        IsBusy = true;
        StatusMessage = null;
        IndexProgress = 0;

        try
        {
            var files = _app.Scan.Index.Values
                .Where(e => !e.IsAttachedOverlay)
                .SelectMany(e => e.Versions)
                .Where(v => v.SourceType != TextureSourceType.Override)
                .ToList();

            var progress = new Progress<IndexProgress>(p => IndexProgress = p.Fraction);
            await Task.Run(() => _app.Signatures.Build(files, progress));
            IndexProgress = 1;

            if (!_app.Signatures.IsReady)
            {
                StatusMessage = "There are no images indexed to search.";
                return;
            }

            // The crop has no alpha - it came off a screen - so every pixel counts.
            var signature = SpriteSignature.FromBgra(query.Bgra, query.Width, query.Height, alphaFloor: 1);

            var matches = await Task.Run(() =>
            {
                var ranked = _finder.RankByPalette(signature, _app.Signatures.Entries);
                var shortlist = ranked.Take(_finder.RefineCount).ToList();

                Parallel.ForEach(shortlist, m =>
                {
                    var candidate = PixelDecoder.Decode(m.FullPath, 128);
                    if (candidate is not null) _finder.Refine(m, query, candidate);
                });

                return SpriteFinder.Order(shortlist);
            });

            Show(matches, files);
        }
        catch (Exception ex)
        {
            StatusMessage = "The search could not finish: " + ex.Message;
        }
        finally
        {
            _hasSearched = true;
            IsBusy = false;
            RaiseResultProperties();
        }
    }

    private void Show(IReadOnlyList<SpriteMatch> matches, IReadOnlyList<TextureFile> files)
    {
        var byPath = new Dictionary<string, TextureFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) byPath.TryAdd(f.FullPath, f);

        Results.Clear();

        foreach (var match in matches.Take(ResultCount))
        {
            if (!byPath.TryGetValue(match.FullPath, out var file)) continue;

            _app.Scan.Index.TryGetValue(file.TextureId, out var entry);
            Results.Add(new IdentifyResultViewModel(match, file, entry));
        }

        if (Results.Count == 0)
        {
            StatusMessage = "Nothing in the library came close. If the character is built "
                            + "from PCC parts, no single file looks like what is on screen - "
                            + "Elin tints those greyscale pieces as it draws them.";
        }
    }

    private void OpenResult(object? parameter)
    {
        if (parameter is IdentifyResultViewModel { Entry: { } entry }) _openDetail(entry);
    }

    private void RaiseResultProperties()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(FoundNothing));
        OnPropertyChanged(nameof(ShowIndexProgress));
    }
}
