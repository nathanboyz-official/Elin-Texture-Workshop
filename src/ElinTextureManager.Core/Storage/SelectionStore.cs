using System.Text.Json;
using System.Text.Json.Serialization;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Storage;

/// <summary>
/// Persists the user's per-texture selections to selections.json.
///
/// Selections are grouped by profile so that presets ("Anime", "Vanilla+") can be added
/// later without a file-format change; v1 uses a single "Default" profile.
/// </summary>
public sealed class SelectionStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private SelectionDocument _doc = new();

    public SelectionStore(string path) => _path = path;

    public string ActiveProfile
    {
        get { lock (_gate) return _doc.ActiveProfile; }
        set { lock (_gate) _doc.ActiveProfile = string.IsNullOrWhiteSpace(value) ? "Default" : value; }
    }

    public IReadOnlyList<string> Profiles
    {
        get { lock (_gate) return _doc.Profiles.Keys.ToList(); }
    }

    private Dictionary<string, OverrideSelection> Current
    {
        get
        {
            if (!_doc.Profiles.TryGetValue(_doc.ActiveProfile, out var map))
            {
                map = new Dictionary<string, OverrideSelection>(StringComparer.OrdinalIgnoreCase);
                _doc.Profiles[_doc.ActiveProfile] = map;
            }
            return map;
        }
    }

    public int Count { get { lock (_gate) return Current.Count; } }

    public IEnumerable<OverrideSelection> All()
    {
        lock (_gate) return Current.Values.OrderBy(s => s.TextureId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public OverrideSelection? Get(string textureId)
    {
        lock (_gate) return Current.GetValueOrDefault(textureId);
    }

    public bool Has(string textureId)
    {
        lock (_gate) return Current.ContainsKey(textureId);
    }

    public void Set(OverrideSelection selection)
    {
        lock (_gate)
        {
            selection.Profile = _doc.ActiveProfile;
            Current[selection.TextureId] = selection;
        }
    }

    public void Remove(string textureId)
    {
        lock (_gate) Current.Remove(textureId);
    }

    public void RemoveAndSave(string textureId)
    {
        Remove(textureId);
        Save();
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _doc = new SelectionDocument();
                    return;
                }

                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<SelectionDocument>(json, JsonOptions);

                _doc = loaded ?? new SelectionDocument();
                if (string.IsNullOrWhiteSpace(_doc.ActiveProfile)) _doc.ActiveProfile = "Default";

                AppLog.Info($"Loaded {Current.Count} selections from {_path}");
            }
            catch (Exception ex)
            {
                // A corrupt file must not stop the app; keep a copy for the user.
                AppLog.Error($"Could not read {_path}; starting with an empty selection set", ex);
                TryQuarantine();
                _doc = new SelectionDocument();
            }
        }
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(_path))
                File.Move(_path, _path + $".corrupt_{DateTime.Now:yyyyMMdd_HHmmss}", overwrite: true);
        }
        catch { }
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_doc, JsonOptions);

                // Write-then-move so an interrupted save cannot corrupt the file.
                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Could not save selections to {_path}", ex);
            }
        }
    }

    /// <summary>How many selections a profile holds, for listing them.</summary>
    public int CountIn(string profile)
    {
        lock (_gate)
            return _doc.Profiles.TryGetValue(profile, out var map) ? map.Count : 0;
    }

    public bool HasProfile(string name)
    {
        lock (_gate) return _doc.Profiles.ContainsKey(name);
    }

    /// <summary>
    /// Adds an empty profile, or one copied from another. Returns false if the name is
    /// already taken - silently merging two profiles would lose a set of choices.
    /// </summary>
    public bool CreateProfile(string name, string? copyFrom = null)
    {
        lock (_gate)
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name) || _doc.Profiles.ContainsKey(name)) return false;

            var map = new Dictionary<string, OverrideSelection>(StringComparer.OrdinalIgnoreCase);

            if (copyFrom is not null && _doc.Profiles.TryGetValue(copyFrom, out var source))
            {
                foreach (var (id, selection) in source)
                    map[id] = Copy(selection, name);
            }

            _doc.Profiles[name] = map;
            return true;
        }
    }

    public bool RenameProfile(string from, string to)
    {
        lock (_gate)
        {
            to = to.Trim();
            if (string.IsNullOrWhiteSpace(to) || from == to) return false;
            if (!_doc.Profiles.TryGetValue(from, out var map)) return false;
            if (_doc.Profiles.ContainsKey(to)) return false;

            _doc.Profiles.Remove(from);
            foreach (var selection in map.Values) selection.Profile = to;
            _doc.Profiles[to] = map;

            if (_doc.ActiveProfile == from) _doc.ActiveProfile = to;
            return true;
        }
    }

    /// <summary>
    /// Deletes a profile. The last one cannot go: with no profiles there is nowhere for
    /// the next selection to live.
    /// </summary>
    public bool DeleteProfile(string name)
    {
        lock (_gate)
        {
            if (_doc.Profiles.Count <= 1 || !_doc.Profiles.Remove(name)) return false;

            if (_doc.ActiveProfile == name) _doc.ActiveProfile = _doc.Profiles.Keys.First();
            return true;
        }
    }

    /// <summary>Replaces a profile's contents wholesale, which is what importing a setup does.</summary>
    public void ReplaceProfile(string name, IEnumerable<OverrideSelection> selections)
    {
        lock (_gate)
        {
            var map = new Dictionary<string, OverrideSelection>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in selections) map[selection.TextureId] = Copy(selection, name);

            _doc.Profiles[name] = map;
        }
    }

    private static OverrideSelection Copy(OverrideSelection s, string profile) => new()
    {
        TextureId = s.TextureId,
        FileName = s.FileName,
        SourceModKey = s.SourceModKey,
        SourceWorkshopId = s.SourceWorkshopId,
        SourceModName = s.SourceModName,
        SourcePath = s.SourcePath,
        SourceHash = s.SourceHash,
        SourceType = s.SourceType,
        Kind = s.Kind,
        SelectedUtc = s.SelectedUtc,
        Profile = profile,
    };

    /// <summary>Exports the active profile's selections for backup or sharing.</summary>
    public string ExportJson()
    {
        lock (_gate)
        {
            var export = new SelectionExport
            {
                Profile = _doc.ActiveProfile,
                ExportedUtc = DateTime.UtcNow,
                Selections = Current.Values
                    .OrderBy(s => s.TextureId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
            return JsonSerializer.Serialize(export, JsonOptions);
        }
    }

    public static SelectionExport? ParseExport(string json)
    {
        try { return JsonSerializer.Deserialize<SelectionExport>(json, JsonOptions); }
        catch (Exception ex)
        {
            AppLog.Error("Could not parse the selection file", ex);
            return null;
        }
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class SelectionDocument
    {
        public string ActiveProfile { get; set; } = "Default";

        public Dictionary<string, Dictionary<string, OverrideSelection>> Profiles { get; set; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Default"] = new(StringComparer.OrdinalIgnoreCase),
            };
    }
}

/// <summary>The shape written to ElinTextureSelections.json.</summary>
public sealed class SelectionExport
{
    public string Profile { get; set; } = "Default";
    public DateTime ExportedUtc { get; set; }
    public string Application { get; set; } = "Elin Texture Manager";
    public int FormatVersion { get; set; } = 1;
    public List<OverrideSelection> Selections { get; set; } = new();
}
