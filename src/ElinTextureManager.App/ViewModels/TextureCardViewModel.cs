using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Overrides;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One tile in the texture grid.</summary>
public sealed class TextureCardViewModel : ObservableObject
{
    private BitmapSource? _thumbnail;
    private bool _thumbnailRequested;

    public TextureCardViewModel(TextureEntry entry, TextureWinner winner, string? alias, bool hasOverride)
    {
        Entry = entry;
        Winner = winner;
        Alias = alias;
        HasOverride = hasOverride;
    }

    public TextureEntry Entry { get; }
    public TextureWinner Winner { get; }
    public string? Alias { get; }
    public bool HasOverride { get; }

    public string TextureId => Entry.TextureId;
    public string Category => Entry.Category;
    public string Prefix => Entry.Prefix;

    /// <summary>The alias when the user has named this texture, otherwise nothing.</summary>
    public string? DisplayName => string.IsNullOrWhiteSpace(Alias) ? null : Alias;

    public bool HasDisplayName => DisplayName is not null;

    public string SourceSummary => Entry.VersionSummary;

    public bool HasConflict => Entry.HasConflict;

    /// <summary>Multiple sources that are all byte-identical: flagged, but not a real choice.</summary>
    public bool IsIdenticalConflict => Entry.AllIdentical;

    public string CurrentLabel => Winner.File is null
        ? "No enabled source"
        : Winner.Description;

    public bool IsUncertain => Winner.Confidence == WinnerConfidence.Unknown;

    /// <summary>The image shown on the card: the winning version.</summary>
    public string? PreviewPath => Winner.File?.FullPath
                                  ?? Entry.Versions.FirstOrDefault()?.FullPath
                                  ?? Entry.Variants.FirstOrDefault()?.FullPath;

    public string DimensionsText => Winner.File?.DimensionsText
                                    ?? Entry.Versions.FirstOrDefault()?.DimensionsText
                                    ?? "unknown";

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>
    /// Loads the thumbnail on demand. The grid is virtualised, so this runs only for
    /// tiles that actually become visible.
    /// </summary>
    public async void EnsureThumbnail(int slotWidth)
    {
        if (_thumbnailRequested) return;
        _thumbnailRequested = true;

        var path = PreviewPath;
        if (path is null) return;

        var sourceWidth = Winner.File?.PixelWidth ?? 0;
        var decodeWidth = TextureImageLoader.DecodeWidthFor(sourceWidth, slotWidth);

        Thumbnail = await TextureImageLoader.LoadAsync(path, decodeWidth);
    }

    /// <summary>Forces a reload, used after an override is written or removed.</summary>
    public void InvalidateThumbnail()
    {
        _thumbnailRequested = false;
        Thumbnail = null;
        OnPropertyChanged(nameof(PreviewPath));
    }

    /// <summary>Matches the free-text search box against ID, alias, mod names and Workshop IDs.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        if (TextureId.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (Entry.NumericId?.ToString().Contains(query, StringComparison.Ordinal) == true) return true;
        if (Alias?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;

        foreach (var v in Entry.AllSources)
        {
            if (v.ModName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            if (v.WorkshopId?.Contains(query, StringComparison.Ordinal) == true) return true;
        }

        return false;
    }
}
