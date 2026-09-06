namespace ElinTextureManager.Core.News;

/// <summary>One announcement from Elin's Steam news feed.</summary>
public sealed class NewsItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;

    /// <summary>Body text with Steam's BBCode markup already stripped.</summary>
    public string Contents { get; set; } = string.Empty;

    /// <summary>Which feed it came from, e.g. "Community Announcements".</summary>
    public string FeedLabel { get; set; } = string.Empty;

    public DateTimeOffset Date { get; set; }

    public string DateText => Date == default ? "unknown" : Date.LocalDateTime.ToString("yyyy-MM-dd");

    /// <summary>
    /// True for the release notes the community cares most about. Patch announcements are
    /// titled "Update <version>" or "... Stable"; anything else is general news.
    /// </summary>
    public bool IsPatchNote =>
        Title.StartsWith("Update ", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("Stable", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("Hotfix", StringComparison.OrdinalIgnoreCase);
}
