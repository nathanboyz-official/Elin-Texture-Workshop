namespace ElinTextureManager.Core.Workshop;

/// <summary>
/// What Steam publicly knows about one Workshop item.
///
/// Only the fields that answer a question the application actually asks. Notably not
/// the description or preview image: those are already on the mod's own page, which is
/// one click away, and holding copies of other people's text is not this tool's job.
/// </summary>
public sealed class WorkshopItem
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    /// <summary>When the author last published a change, as Unix seconds.</summary>
    public long TimeUpdatedUnix { get; init; }

    public DateTime? TimeUpdatedUtc => TimeUpdatedUnix <= 0
        ? null
        : DateTimeOffset.FromUnixTimeSeconds(TimeUpdatedUnix).UtcDateTime;

    /// <summary>Workshop tags as Steam holds them, which is where mod sections come from.</summary>
    public List<string> Tags { get; } = new();

    /// <summary>
    /// Steam's own result code for this id. Anything but 1 means the item could not be
    /// read - most often because it has been removed from the Workshop.
    /// </summary>
    public int Result { get; init; }

    public bool Banned { get; init; }

    /// <summary>0 is public. Anything else means it is no longer generally available.</summary>
    public int Visibility { get; init; }

    /// <summary>
    /// True when the item is gone or hidden. This is the answer to the load-order
    /// entries that point at folders which no longer exist: the author unlisted it, or
    /// Steam removed it, and the local copy went with it.
    /// </summary>
    public bool IsUnavailable => Result != 1 || Banned || Visibility != 0;
}

/// <summary>What one lookup produced.</summary>
public sealed class WorkshopFetchResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<WorkshopItem> Items { get; } = new();
}
