using System.Text.Json;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Workshop;

/// <summary>What one lookup returned, kept so the answer survives being offline.</summary>
public sealed class WorkshopCacheDocument
{
    public int FormatVersion { get; set; } = 1;
    public DateTime FetchedUtc { get; set; }
    public List<WorkshopCacheEntry> Items { get; set; } = new();
}

/// <summary>A cache row. Flat on purpose - it is written and read as plain JSON.</summary>
public sealed class WorkshopCacheEntry
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public long TimeUpdatedUnix { get; set; }
    public int Result { get; set; }
    public bool Banned { get; set; }
    public int Visibility { get; set; }
    public List<string> Tags { get; set; } = new();
}

public static class WorkshopCache
{
    public static void Save(string path, IEnumerable<WorkshopItem> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var document = new WorkshopCacheDocument { FetchedUtc = DateTime.UtcNow };

            foreach (var item in items)
            {
                var entry = new WorkshopCacheEntry
                {
                    Id = item.Id,
                    Title = item.Title,
                    TimeUpdatedUnix = item.TimeUpdatedUnix,
                    Result = item.Result,
                    Banned = item.Banned,
                    Visibility = item.Visibility,
                };
                entry.Tags.AddRange(item.Tags);
                document.Items.Add(entry);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not save the workshop cache to {path}", ex);
        }
    }

    public static (DateTime FetchedUtc, List<WorkshopItem> Items)? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var document = JsonSerializer.Deserialize<WorkshopCacheDocument>(
                File.ReadAllText(path), Options);

            if (document is null) return null;

            var items = new List<WorkshopItem>(document.Items.Count);

            foreach (var entry in document.Items)
            {
                if (string.IsNullOrEmpty(entry.Id)) continue;

                var item = new WorkshopItem
                {
                    Id = entry.Id,
                    Title = entry.Title,
                    TimeUpdatedUnix = entry.TimeUpdatedUnix,
                    Result = entry.Result,
                    Banned = entry.Banned,
                    Visibility = entry.Visibility,
                };
                item.Tags.AddRange(entry.Tags);
                items.Add(item);
            }

            return (document.FetchedUtc, items);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read the workshop cache at {path}", ex);
            return null;
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
}
