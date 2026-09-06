using System.Text.Json;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Overrides;

namespace ElinTextureManager.Core.Storage;

public enum ThumbnailSize { Small = 96, Medium = 144, Large = 208 }

/// <summary>
/// Persisted application state. Stored in %APPDATA%\ElinTextureManager, never inside
/// a Steam Workshop folder.
/// </summary>
public sealed class AppSettings
{
    public string? ElinPath { get; set; }
    public string? WorkshopPath { get; set; }

    public bool AutoRefreshWorkshop { get; set; } = true;
    public bool WatchFileChanges { get; set; } = true;
    public bool CreateLoadOrderBackups { get; set; } = true;

    /// <summary>
    /// Also copy selected textures into Elin\User\Texture Replace. Off by default;
    /// a fallback for users whose package overrides do not take effect.
    /// </summary>
    public bool MirrorOverridesToUserFolder { get; set; }

    public ThumbnailSize ThumbnailSize { get; set; } = ThumbnailSize.Medium;

    /// <summary>Which end of loadorder.txt wins a conflict.</summary>
    public PriorityConvention PriorityConvention { get; set; } = PriorityConvention.LaterWins;

    public int OverrideLoadPriority { get; set; } = OverrideManager.DefaultLoadPriority;

    public bool FirstRunCompleted { get; set; }

    // Window and view state
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
    public string LastPage { get; set; } = "AllTextures";
    public string LastFilter { get; set; } = "All";

    /// <summary>Section selected on the Mods page, e.g. "Characters" or "Sprite".</summary>
    public string ModsSection { get; set; } = "All";

    /// <summary>Most Workshop items are not texture packs, so the Mods page filters them out.</summary>
    public bool ModsTexturesOnly { get; set; } = true;

    /// <summary>
    /// Whether the News page may contact Steam. This is the only network access the
    /// application makes; turning it off leaves the page showing the last cached fetch.
    /// </summary>
    public bool EnableSteamNews { get; set; } = true;

    /// <summary>
    /// Whether to ask Steam what it publishes for the installed Workshop items.
    ///
    /// Off unless asked for. It needs no account and sends nothing but the Workshop IDs
    /// already visible in every mod URL, but it is still the library leaving the machine,
    /// and that should be the user deciding rather than a default.
    /// </summary>
    public bool EnableWorkshopChecks { get; set; }

    /// <summary>
    /// Whether closing the window hides it to the notification area instead of quitting.
    ///
    /// On by default: this is a tool people dip in and out of while the game is running,
    /// and a full rescan of a large library on every launch is a poor way to answer a
    /// question you only just thought of.
    /// </summary>
    public bool MinimiseToTray { get; set; } = true;

    /// <summary>
    /// Colours the user has kept, shown after the built-in palette.
    ///
    /// Six hex digits each, the same as a style stores. Theirs to remove; the built-in
    /// ones are not.
    /// </summary>
    public List<string> SavedColours { get; set; } = new();

    /// <summary>
    /// PCC parts the user has starred, as "layer|set|id".
    ///
    /// Kept as ids rather than paths so a favourite survives the mod being updated,
    /// moved, or reinstalled somewhere else.
    /// </summary>
    public List<string> FavouriteParts { get; set; } = new();

    /// <summary>Whether the "it is still running down here" hint has been shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>
    /// Open Workshop links in the Steam desktop client rather than a browser. This is a
    /// local protocol hand-off and needs no account or sign-in; off sends them to the
    /// browser instead. Ignored when Steam is not installed.
    /// </summary>
    public bool OpenWorkshopInSteamApp { get; set; } = true;

    public string ActiveProfile { get; set; } = "Default";

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, SelectionStore.JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read settings from {path}; using defaults", ex);
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, SelectionStore.JsonOptions);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not save settings to {path}", ex);
        }
    }
}

/// <summary>
/// User-assigned display names for texture IDs (objC_2115 -> "Gaki").
/// Kept separate from selections so aliases survive clearing overrides.
/// </summary>
public sealed class AliasStore
{
    private readonly string _path;
    private Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public AliasStore(string path) => _path = path;

    public int Count => _aliases.Count;

    public string? Get(string textureId) =>
        _aliases.TryGetValue(textureId, out var name) ? name : null;

    public void Set(string textureId, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) _aliases.Remove(textureId);
        else _aliases[textureId] = displayName.Trim();
    }

    public IReadOnlyDictionary<string, string> All => _aliases;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                json, SelectionStore.JsonOptions);

            if (loaded is not null)
                _aliases = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);

            AppLog.Info($"Loaded {_aliases.Count} texture aliases.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read aliases from {_path}", ex);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_aliases, SelectionStore.JsonOptions);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not save aliases to {_path}", ex);
        }
    }
}
