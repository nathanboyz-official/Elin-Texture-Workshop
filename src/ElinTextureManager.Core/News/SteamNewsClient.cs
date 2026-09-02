using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.News;

/// <summary>
/// Reads Elin's announcements from Steam's public news endpoint.
///
/// This is the only part of the application that touches the network, and it is opt-out
/// in Settings. It sends no identifying information, needs no API key, and asks only for
/// the news of app 2135150. The last successful response is cached on disk so the page
/// still has something to show while offline.
/// </summary>
public sealed class SteamNewsClient
{
    public const int ElinAppId = 2135150;

    private const string Endpoint =
        "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/"
        + "?appid={0}&count={1}&maxlength={2}";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ElinTextureManager/1.0");
        return client;
    }

    /// <summary>
    /// Fetches the newest announcements. Never throws: a failure is logged and reported
    /// through the result so the page can fall back to the cache.
    /// </summary>
    public async Task<NewsFetchResult> FetchAsync(
        int count = 20, int maxLength = 1200, CancellationToken ct = default)
    {
        var url = string.Format(Endpoint, ElinAppId, count, maxLength);

        try
        {
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            var items = Parse(json);

            AppLog.Info($"Steam news fetched: {items.Count} items.");
            return new NewsFetchResult(items, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not fetch Steam news: {ex.Message}");
            return new NewsFetchResult(Array.Empty<NewsItem>(), ex.Message);
        }
    }

    public static IReadOnlyList<NewsItem> Parse(string json)
    {
        var payload = JsonSerializer.Deserialize<AppNewsEnvelope>(json);
        var raw = payload?.AppNews?.NewsItems;
        if (raw is null) return Array.Empty<NewsItem>();

        var list = new List<NewsItem>(raw.Count);

        foreach (var item in raw)
        {
            list.Add(new NewsItem
            {
                Id = item.Gid ?? string.Empty,
                Title = (item.Title ?? string.Empty).Trim(),
                Url = item.Url ?? string.Empty,
                Author = (item.Author ?? string.Empty).Trim(),
                FeedLabel = (item.FeedLabel ?? string.Empty).Trim(),
                Contents = StripBbCode(item.Contents),
                Date = item.Date > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(item.Date)
                    : default,
            });
        }

        return list;
    }

    /// <summary>
    /// Turns Steam's BBCode body into readable plain text. Tags are removed rather than
    /// rendered - the panel is a summary, and the full post is one click away.
    /// </summary>
    public static string StripBbCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        var depth = 0;

        foreach (var c in raw)
        {
            if (c == '[') { depth++; continue; }
            if (c == ']') { if (depth > 0) depth--; continue; }
            if (depth == 0) sb.Append(c);
        }

        // Collapse the runs of blank lines Steam's markup leaves behind.
        var text = sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        var outLines = new List<string>(lines.Length);
        var blank = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (blank) continue;
                blank = true;
            }
            else blank = false;

            outLines.Add(trimmed);
        }

        return string.Join(Environment.NewLine, outLines).Trim();
    }

    // ---- wire format ----

    private sealed class AppNewsEnvelope
    {
        [JsonPropertyName("appnews")] public AppNewsBody? AppNews { get; set; }
    }

    private sealed class AppNewsBody
    {
        [JsonPropertyName("newsitems")] public List<RawNewsItem>? NewsItems { get; set; }
    }

    private sealed class RawNewsItem
    {
        [JsonPropertyName("gid")] public string? Gid { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("contents")] public string? Contents { get; set; }
        [JsonPropertyName("feedlabel")] public string? FeedLabel { get; set; }
        [JsonPropertyName("date")] public long Date { get; set; }
    }
}

public sealed record NewsFetchResult(IReadOnlyList<NewsItem> Items, string? Error)
{
    public bool Success => Error is null;
}
