using System.Text.Json;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.News;

/// <summary>
/// The last successful news response, kept on disk so the page has something to show
/// before the first fetch returns and while offline. Stored in %APPDATA%, never in the
/// game folder.
/// </summary>
public static class NewsCache
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public sealed class Document
    {
        public DateTimeOffset FetchedUtc { get; set; }
        public List<NewsItem> Items { get; set; } = new();
    }

    public static Document? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read the news cache at {path}: {ex.Message}");
            return null;
        }
    }

    public static void Save(string path, IReadOnlyList<NewsItem> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var doc = new Document { FetchedUtc = DateTimeOffset.UtcNow, Items = items.ToList() };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(doc, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not write the news cache to {path}: {ex.Message}");
        }
    }
}
