using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.App.Services;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Services;

namespace ElinTextureManager.App.ViewModels;

/// <summary>A row on the Mods page.</summary>
public sealed class ModRowViewModel : ObservableObject
{
    private BitmapSource? _preview;

    public ModRowViewModel(ModPackage mod, int conflictCount, int uniqueCount)
    {
        Mod = mod;
        ConflictCount = conflictCount;
        UniqueCount = uniqueCount;
        LoadPreview();
    }

    public ModPackage Mod { get; }

    public string Name => Mod.Name;
    public string? WorkshopId => Mod.WorkshopId;
    public string Directory => Mod.Directory;
    public string? Author => Mod.Author;
    public int TextureCount => Mod.TextureCount;
    public int ConflictCount { get; }
    public int UniqueCount { get; }
    public bool Enabled => Mod.Enabled;
    public bool HasTextures => Mod.HasTextureReplacements;
    public bool MetadataMissing => Mod.MetadataMissing;

    public string LoadOrderText => Mod.InLoadOrderFile
        ? $"#{Mod.LoadOrderIndex + 1}"
        : Mod.SourceType switch
        {
            TextureSourceType.Override => "override",
            TextureSourceType.LocalMod => "local package",
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
}

/// <summary>The Mods page: every detected mod, with texture and conflict counts.</summary>
public sealed class ModsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private string _searchText = string.Empty;
    private bool _texturesOnly = true;

    public ModsViewModel(AppServices app, Action<ModPackage> openMod)
    {
        _app = app;

        OpenCommand = new RelayCommand(p =>
        {
            if (p is ModRowViewModel row) openMod(row.Mod);
        });

        OpenFolderCommand = new RelayCommand(p =>
            ShellService.OpenFolder((p as ModRowViewModel)?.Directory));
    }

    public ObservableCollection<ModRowViewModel> Items { get; } = new();

    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Apply(); }
    }

    /// <summary>Most Workshop items are not texture packs, so this defaults to on.</summary>
    public bool TexturesOnly
    {
        get => _texturesOnly;
        set { if (SetProperty(ref _texturesOnly, value)) Apply(); }
    }

    public int ResultCount => Items.Count;

    /// <summary>Scroll position, kept across navigation. See TextureBrowserViewModel.</summary>
    public double ScrollOffset { get; set; }

    public void Apply(bool preserveScroll = false)
    {
        if (!preserveScroll) ScrollOffset = 0;

        Items.Clear();

        // Conflict counts per mod: how many of its textures another enabled mod also ships.
        var conflictsByMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var uniqueByMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _app.Scan.Index.Values)
        {
            foreach (var v in entry.Versions)
            {
                var map = entry.HasConflict ? conflictsByMod : uniqueByMod;
                map[v.ModKey] = map.GetValueOrDefault(v.ModKey) + 1;
            }
        }

        var mods = _app.Scan.Mods.AsEnumerable();
        if (_texturesOnly) mods = mods.Where(m => m.HasTextureReplacements);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var q = _searchText;
            mods = mods.Where(m =>
                m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.WorkshopId?.Contains(q, StringComparison.Ordinal) == true
                || m.Author?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
        }

        foreach (var mod in mods
                     .OrderByDescending(m => m.TextureCount)
                     .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Items.Add(new ModRowViewModel(
                mod,
                conflictsByMod.GetValueOrDefault(mod.Key),
                uniqueByMod.GetValueOrDefault(mod.Key)));
        }

        OnPropertyChanged(nameof(ResultCount));
    }
}
