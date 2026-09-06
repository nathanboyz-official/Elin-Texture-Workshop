using System.Net.Http;
using System.Text.Json;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Workshop;

/// <summary>
/// Asks Steam what it knows about installed Workshop items.
///
/// The endpoint is public and takes no API key, so there is no account to connect, no
/// token to store and nothing to log in to - which matters for a tool people are meant
/// to be able to read the source of and trust. The request body is a list of Workshop
/// IDs and nothing else: no user id, no machine id, no library contents beyond the ids
/// themselves, which are public numbers that appear in every mod's URL.
///
/// It is still a network call, so it is off unless the user turns it on.
/// </summary>
public sealed class WorkshopClient
{
    private const string Endpoint =
        "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    /// <summary>
    /// Ids per request. Steam accepts large batches, but a smaller one fails smaller:
    /// a dropped connection costs one chunk rather than the whole library.
    /// </summary>
    private const int BatchSize = 100;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ElinTextureWorkshop/1.0");
        return client;
    }

    /// <summary>
    /// Looks up every id, in batches. Never throws: a failure is reported through the
    /// result so the caller can fall back to whatever it had cached.
    /// </summary>
    public async Task<WorkshopFetchResult> FetchAsync(IEnumerable<string> workshopIds,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var ids = workshopIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new WorkshopFetchResult { Success = true };
        if (ids.Count == 0) return result;

        var done = 0;

        for (var offset = 0; offset < ids.Count; offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = ids.Skip(offset).Take(BatchSize).ToList();

            try
            {
                var fields = new List<KeyValuePair<string, string>>
                {
                    new("itemcount", batch.Count.ToString()),
                };

                for (var i = 0; i < batch.Count; i++)
                    fields.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", batch[i]));

                using var content = new FormUrlEncodedContent(fields);
                using var response = await Http.PostAsync(Endpoint, content, ct).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                result.Items.AddRange(Parse(json));

                done += batch.Count;
                progress?.Report(done);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Could not read Workshop details for {batch.Count} items", ex);

                // Partial results are still worth having: the mods that did come back
                // can be reported on, and the rest simply stay unknown.
                return new WorkshopFetchResult { Success = false, Error = ex.Message }
                    .With(result.Items);
            }
        }

        return result;
    }

    /// <summary>
    /// Turns Steam's reply into items. Public because it is a pure function over a
    /// documented response shape, and worth pinning down with tests on its own.
    /// </summary>
    public static List<WorkshopItem> Parse(string json)
    {
        var items = new List<WorkshopItem>();

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("response", out var response)) return items;
        if (!response.TryGetProperty("publishedfiledetails", out var details)) return items;

        foreach (var entry in details.EnumerateArray())
        {
            var id = entry.TryGetProperty("publishedfileid", out var idElement)
                ? idElement.GetString()
                : null;

            if (string.IsNullOrEmpty(id)) continue;

            var item = new WorkshopItem
            {
                Id = id,
                Title = entry.TryGetProperty("title", out var t) ? t.GetString() : null,
                TimeUpdatedUnix = entry.TryGetProperty("time_updated", out var u)
                                  && u.TryGetInt64(out var unix) ? unix : 0,
                Result = entry.TryGetProperty("result", out var r) && r.TryGetInt32(out var code) ? code : 0,
                Banned = entry.TryGetProperty("banned", out var b) && b.TryGetInt32(out var banned) && banned != 0,
                Visibility = entry.TryGetProperty("visibility", out var v)
                             && v.TryGetInt32(out var vis) ? vis : 0,
            };

            if (entry.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    if (tag.TryGetProperty("tag", out var name) && name.GetString() is { } text
                        && !string.IsNullOrWhiteSpace(text))
                    {
                        item.Tags.Add(text);
                    }
                }
            }

            items.Add(item);
        }

        return items;
    }
}

internal static class WorkshopFetchResultExtensions
{
    /// <summary>Carries partial results onto a failed fetch.</summary>
    public static WorkshopFetchResult With(this WorkshopFetchResult result,
        IEnumerable<WorkshopItem> items)
    {
        result.Items.AddRange(items);
        return result;
    }
}
