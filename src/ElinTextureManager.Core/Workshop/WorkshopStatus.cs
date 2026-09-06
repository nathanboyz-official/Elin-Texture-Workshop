using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Workshop;

/// <summary>How an installed mod compares with what Steam currently publishes.</summary>
public enum WorkshopState
{
    /// <summary>Nothing has been fetched for this mod.</summary>
    Unknown = 0,

    /// <summary>The local copy is as new as what Steam publishes.</summary>
    UpToDate = 1,

    /// <summary>Steam published a change after the local folder was last written.</summary>
    UpdateWaiting = 2,

    /// <summary>Removed, hidden, or banned - the page is no longer generally available.</summary>
    Gone = 3,
}

public static class WorkshopStatus
{
    /// <summary>
    /// How long Steam has to be ahead before it counts as an update.
    ///
    /// Clock skew and the gap between Steam writing files and stamping the folder are
    /// both measured in minutes. An hour is comfortably past both, and on a real library
    /// of 333 mods it produced two results rather than a wall of them.
    /// </summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromHours(1);

    public static WorkshopState For(ModPackage mod, WorkshopItem? item)
    {
        if (item is null) return WorkshopState.Unknown;
        if (item.IsUnavailable) return WorkshopState.Gone;
        if (item.TimeUpdatedUtc is not { } published) return WorkshopState.Unknown;
        if (mod.LastModifiedUtc == default) return WorkshopState.Unknown;

        return published - mod.LastModifiedUtc > Tolerance
            ? WorkshopState.UpdateWaiting
            : WorkshopState.UpToDate;
    }
}
