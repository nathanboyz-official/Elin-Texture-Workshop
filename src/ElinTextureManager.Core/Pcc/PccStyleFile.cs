using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// Reads and writes the style files Elin's own character editor uses.
///
/// The format is small and odd in one way worth knowing: it carries Unity's "$id"
/// reference bookkeeping, and the five favourite slots wrap their map in an extra "data"
/// object while the named exports do not. Both shapes are read; exports are written in
/// the shape the export button writes, so a file made here is a file the game's Import
/// button accepts.
///
///   { "$id": "1", "map": { "$id": "2", "hair": [ "female", "3", "7C8FAE" ] } }
///
/// Each entry is [ set, id, colour ], where set is the folder under Actor/PCC, id is the
/// tail of pcc_layer_id.png, and colour is six hex digits or null for untinted.
/// </summary>
public static class PccStyleFile
{
    /// <summary>Where Elin keeps them, relative to the game folder.</summary>
    public const string FolderName = "PCC";

    /// <summary>The favourite slots the editor's Register buttons write.</summary>
    public const int FavouriteSlots = 5;

    public static string FolderIn(string elinRoot) => Path.Combine(elinRoot, "User", FolderName);

    public static string FavouritePath(string elinRoot, int slot) =>
        Path.Combine(FolderIn(elinRoot), $"fav{slot}");

    /// <summary>
    /// Every style in the game's folder: the five favourite slots and any named export.
    /// Unreadable files are skipped rather than failing the lot.
    /// </summary>
    public static List<PccStyle> ReadFolder(string elinRoot)
    {
        var styles = new List<PccStyle>();
        var folder = FolderIn(elinRoot);

        if (!Directory.Exists(folder)) return styles;

        foreach (var path in Directory.GetFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var style = Read(path);
            if (style is not null) styles.Add(style);
        }

        return styles;
    }

    public static PccStyle? Read(string path)
    {
        try
        {
            var style = Parse(File.ReadAllText(path));
            if (style is null) return null;

            style.SourcePath = path;
            style.Name = NameOf(path);
            return style;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read the character style at {path}", ex);
            return null;
        }
    }

    /// <summary>A fav slot's file has no extension and no name inside it, so it gets one.</summary>
    private static string NameOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        if (name.StartsWith("fav", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[3..], out var slot))
        {
            // The game's buttons are one-based; the files are zero-based.
            return $"Favourite {slot + 1}";
        }

        return name;
    }

    public static PccStyle? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;

            // Favourite slots nest the map one deeper than exports do.
            var map = root["map"] as JsonObject
                      ?? (root["data"] as JsonObject)?["map"] as JsonObject;

            if (map is null) return null;

            var style = new PccStyle();

            foreach (var (key, value) in map)
            {
                // Unity's own reference bookkeeping, not a layer.
                if (key.StartsWith('$')) continue;
                if (value is not JsonArray entry || entry.Count < 2) continue;

                var id = entry[1]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id)) continue;

                style.Parts[key] = new PccChoice
                {
                    Set = entry[0]?.GetValue<string>() ?? PccSlots.DefaultSet,
                    Id = id,
                    Colour = entry.Count > 2 ? entry[2]?.GetValue<string>() : null,
                };
            }

            return style.Parts.Count == 0 ? null : style;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not read a character style", ex);
            return null;
        }
    }

    /// <summary>
    /// Writes the shape the game's own export button writes.
    ///
    /// The "$id" fields are kept because they are what the game emits, and a file that
    /// differs from the game's own output is a file nobody can be sure it will read.
    /// </summary>
    public static string Write(PccStyle style, bool favouriteSlot = false)
    {
        var map = new JsonObject { ["$id"] = favouriteSlot ? "3" : "2" };

        // Written in the editor's own order rather than however the dictionary happens
        // to enumerate, so two saves of the same character produce the same file.
        foreach (var slot in PccSlots.All)
            Add(slot.Layer, style.Get(slot.Layer));

        // Anything the editor has no row for is carried through untouched. Styles saved
        // by older versions of the game contain a "leg" layer that no longer has a
        // slider; dropping it on save would quietly rewrite a character the user never
        // asked to change.
        foreach (var (layer, choice) in style.Parts)
            if (PccSlots.For(layer) is null) Add(layer, choice);

        void Add(string layer, PccChoice? choice)
        {
            if (choice is null || string.IsNullOrEmpty(choice.Id)) return;
            if (map.ContainsKey(layer)) return;

            map[layer] = new JsonArray(choice.Set, choice.Id,
                choice.Colour is null ? null : JsonValue.Create(choice.Colour));
        }

        JsonObject root = favouriteSlot
            ? new JsonObject { ["$id"] = "1", ["data"] = new JsonObject { ["$id"] = "2", ["map"] = map } }
            : new JsonObject { ["$id"] = "1", ["map"] = map };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Saves a style where the game will find it. Returns the path written.</summary>
    public static string Save(string elinRoot, PccStyle style, string fileName)
    {
        var folder = FolderIn(elinRoot);
        Directory.CreateDirectory(folder);

        var favourite = fileName.StartsWith("fav", StringComparison.OrdinalIgnoreCase)
                        && !fileName.Contains('.');

        var path = Path.Combine(folder, favourite ? fileName : Path.ChangeExtension(fileName, ".txt"));

        // Written then moved, so an interrupted save cannot leave the game reading half
        // a file it will refuse.
        var temp = path + ".tmp";
        File.WriteAllText(temp, Write(style, favourite), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);

        AppLog.Info($"Wrote character style {path}");
        return path;
    }
}
