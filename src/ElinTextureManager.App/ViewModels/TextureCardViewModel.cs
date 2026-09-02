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

    public TextureCardViewModel(
        TextureEntry entry, TextureWinner winner, string? alias, bool hasOverride, int slotWidth)
    {
        Entry = entry;
        Winner = winner;
        Alias = alias;
        HasOverride = hasOverride;
        SlotWidth = slotWidth;
    }

    /// <summary>Width of the tile, used to pick a decode size.</summary>
    public int SlotWidth { get; }

    public TextureEntry Entry { get; }
    public TextureWinner Winner { get; }
    public string? Alias { get; }
    public bool HasOverride { get; }

    /// <summary>The ID as shown on the tile: portrait IDs without their index namespace.</summary>
    public string TextureId => Entry.DisplayId;

    public string Category => Entry.Category;
    public string Prefix => Entry.Prefix;

    /// <summary>Portraits are replaced by file name rather than by sprite index.</summary>
    public bool IsPortrait => Entry.Kind == ReplacementKind.Portrait;

    /// <summary>
    /// True when this portrait has an "-overlay" layer folded into it. The overlay has no
    /// tile of its own; it lives on this portrait's page.
    /// </summary>
    public bool HasOverlay => Entry.HasOverlay;

    /// <summary>
    /// The two right-hand badges share a slot, and knowing a texture is overridden matters
    /// more than knowing it has an overlay, so the overlay badge yields.
    /// </summary>
    public bool ShowOverlayBadge => HasOverlay && !HasOverride;

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

    /// <summary>
    /// The tile image. Reading it starts the load, so the work happens exactly when a
    /// tile is bound for display and never for the thousands of tiles that are not.
    ///
    /// The trigger is the binding rather than the container's Loaded event because the
    /// grid virtualises: containers get reused for different items, and a reused
    /// container raises no second Loaded, which would leave recycled tiles blank.
    /// </summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            EnsureThumbnail();
            return _thumbnail;
        }
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>Loads the thumbnail once, off the UI thread.</summary>
    public async void EnsureThumbnail()
    {
        if (_thumbnailRequested) return;
        _thumbnailRequested = true;

        var path = PreviewPath;
        if (path is null) return;

        var sourceWidth = Winner.File?.PixelWidth ?? 0;
        var decodeWidth = TextureImageLoader.DecodeWidthFor(sourceWidth, SlotWidth);

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
