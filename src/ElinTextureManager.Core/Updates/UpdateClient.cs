using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Updates;

/// <summary>A published release of this application, as GitHub describes it.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Notes,
    string PageUrl,
    string AssetName,
    string DownloadUrl,
    long Size,
    string? Sha256);

public sealed record UpdateCheckResult(ReleaseInfo? Latest, bool IsNewer, string? Error);

/// <summary>
/// Asks GitHub for the newest release of this application and downloads it.
///
/// Talks to the project's own repository and nothing else: one unauthenticated request to
/// api.github.com, and the download itself from github.com. The download is refused unless
/// it comes from this repository's releases, is the size GitHub says, and - when GitHub
/// publishes one, which it does for every upload since mid-2025 - matches its SHA-256.
/// </summary>
public sealed class UpdateClient
{
    public const string Owner = "nathanboyz-official";
    public const string Repository = "Elin-Texture-Workshop";

    public static string ReleasesPage => $"https://github.com/{Owner}/{Repository}/releases/latest";

    private static string LatestEndpoint =>
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    private static string DownloadPrefix =>
        $"https://github.com/{Owner}/{Repository}/releases/download/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // No overall timeout: the zip is tens of megabytes and a slow line is not a failure.
        // The check itself is bounded separately below.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ElinTextureWorkshop-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The version of the running application.</summary>
    public static Version CurrentVersion =>
        Normalise((System.Reflection.Assembly.GetEntryAssembly() ?? typeof(UpdateClient).Assembly)
            .GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Fetches the latest release and compares it with <paramref name="current"/>. Never
    /// throws for network trouble; the error comes back in the result.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            var json = await Http.GetStringAsync(LatestEndpoint, timeout.Token).ConfigureAwait(false);
            var latest = Parse(json);

            if (latest is null)
                return new UpdateCheckResult(null, false, "The latest release has no Windows download.");

            var newer = latest.Version > Normalise(current);
            AppLog.Info($"Update check: running {Normalise(current)}, latest {latest.Version}.");
            return new UpdateCheckResult(latest, newer, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not check for updates: {ex.Message}");
            return new UpdateCheckResult(null, false, ex is OperationCanceledException
                ? "GitHub did not answer in time."
                : ex.Message);
        }
    }

    /// <summary>Reads GitHub's release JSON. Null when it carries no usable Windows build.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var version = ParseTag(tag);
        if (tag is null || version is null) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            if (name is null || url is null) continue;
            if (!IsWindowsBuild(name) || !IsFromThisRepository(url)) continue;

            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;

            string? sha = null;
            if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = digest["sha256:".Length..].Trim().ToLowerInvariant();

            return new ReleaseInfo(
                version,
                tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage,
                name,
                url,
                size,
                sha);
        }

        return null;
    }

    /// <summary>"v1.2.3" or "1.2.3" to a three-part version; null for anything else.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var v) ? Normalise(v) : null;
    }

    /// <summary>Drops the fourth part, which tags never carry, so 1.0.2 equals 1.0.2.0.</summary>
    public static Version Normalise(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static bool IsWindowsBuild(string assetName) =>
        assetName.StartsWith("ElinTextureWorkshop-", StringComparison.OrdinalIgnoreCase)
        && assetName.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsFromThisRepository(string url) =>
        url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads the release zip to <paramref name="destination"/> and checks it. A file
    /// that fails a check is deleted rather than left where something might run it.
    /// </summary>
    public async Task DownloadAsync(ReleaseInfo release, string destination,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsFromThisRepository(release.DownloadUrl))
            throw new InvalidOperationException("The download does not come from this application's releases.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        try
        {
            using (var response = await Http.GetAsync(release.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? release.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(destination);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            Verify(release, destination);
            AppLog.Info($"Downloaded {release.AssetName} ({new FileInfo(destination).Length:N0} bytes).");
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>Throws when the file is not the one GitHub described.</summary>
    public static void Verify(ReleaseInfo release, string file)
    {
        var length = new FileInfo(file).Length;
        if (release.Size > 0 && length != release.Size)
            throw new InvalidDataException(
                $"The download is {length:N0} bytes but GitHub lists {release.Size:N0}.");

        if (release.Sha256 is null) return;

        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != release.Sha256)
            throw new InvalidDataException("The download does not match the checksum GitHub publishes for it.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
