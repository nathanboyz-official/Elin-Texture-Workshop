using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Workshop;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Reading what Steam publishes about installed Workshop items.
///
/// The parsing is tested against the shape Steam actually returns rather than a shape
/// invented here, and the comparison is tested for the thing that would ruin it: saying
/// an update is waiting when one is not. On a library of 333 mods the real answer was
/// two, and a rule that produced two hundred would be worth nothing.
/// </summary>
public sealed class WorkshopTests
{
    /// <summary>Trimmed from a real response for Texture Expand, fields and all.</summary>
    private const string RealResponse = """
    {"response":{"result":1,"resultcount":1,"publishedfiledetails":[{
      "publishedfileid":"3375278123","result":1,"creator":"76561198043111431",
      "creator_app_id":2135150,"consumer_app_id":2135150,"file_size":"309140",
      "title":"Texture Expand","description":"The character's texture changes.",
      "time_created":1733021467,"time_updated":1734773844,"visibility":0,"banned":0,
      "ban_reason":"","subscriptions":21910,"favorited":1480,"views":42661,
      "tags":[{"tag":"Utility"},{"tag":"Sprite"}]}]}}
    """;

    private static ModPackage Mod(DateTime localWrite, string workshopId = "3375278123") => new()
    {
        Key = workshopId,
        Name = "Texture Expand",
        Directory = @"C:\mods\" + workshopId,
        SourceType = TextureSourceType.Workshop,
        WorkshopId = workshopId,
        LastModifiedUtc = localWrite,
    };

    private static WorkshopItem Parsed() => Assert.Single(WorkshopClient.Parse(RealResponse));

    [Fact]
    public void It_reads_the_fields_it_needs_out_of_a_real_response()
    {
        var item = Parsed();

        Assert.Equal("3375278123", item.Id);
        Assert.Equal("Texture Expand", item.Title);
        Assert.Equal(1734773844, item.TimeUpdatedUnix);
        Assert.Equal(new DateTime(2024, 12, 21, 9, 37, 24, DateTimeKind.Utc), item.TimeUpdatedUtc);
        Assert.Equal(new[] { "Utility", "Sprite" }, item.Tags);
        Assert.False(item.IsUnavailable);
    }

    [Fact]
    public void A_response_that_is_not_what_was_expected_yields_nothing_rather_than_throwing()
    {
        Assert.Empty(WorkshopClient.Parse("""{"response":{"result":1}}"""));
        Assert.Empty(WorkshopClient.Parse("{}"));
        Assert.Empty(WorkshopClient.Parse("""{"response":{"publishedfiledetails":[]}}"""));

        // An entry with no id cannot be matched to anything, so it is dropped.
        Assert.Empty(WorkshopClient.Parse("""{"response":{"publishedfiledetails":[{"title":"x"}]}}"""));
    }

    [Fact]
    public void A_copy_older_than_what_Steam_publishes_has_an_update_waiting()
    {
        var item = Parsed();
        var mod = Mod(item.TimeUpdatedUtc!.Value.AddDays(-3));

        Assert.Equal(WorkshopState.UpdateWaiting, WorkshopStatus.For(mod, item));
    }

    [Fact]
    public void A_copy_downloaded_long_after_publication_is_up_to_date()
    {
        // The ordinary case, and the one that has to be right: 155 of this user's mods
        // were last published well over a year before they subscribed. Reading that as
        // an update would flag half the library.
        var item = Parsed();
        var mod = Mod(item.TimeUpdatedUtc!.Value.AddYears(2));

        Assert.Equal(WorkshopState.UpToDate, WorkshopStatus.For(mod, item));
    }

    [Fact]
    public void Minutes_of_difference_are_not_an_update()
    {
        // Steam stamps the folder around the time it writes the files, not to the
        // second, and clocks drift. Anything inside the tolerance is the same version.
        var item = Parsed();
        var mod = Mod(item.TimeUpdatedUtc!.Value.AddMinutes(-20));

        Assert.Equal(WorkshopState.UpToDate, WorkshopStatus.For(mod, item));
    }

    [Fact]
    public void A_mod_nothing_is_known_about_is_unknown_rather_than_up_to_date()
    {
        // Before the user has ever allowed the check, every mod is in this state, and
        // calling that "up to date" would be a claim nobody has earned.
        Assert.Equal(WorkshopState.Unknown, WorkshopStatus.For(Mod(DateTime.UtcNow), null));
        Assert.Equal(WorkshopState.Unknown,
            WorkshopStatus.For(Mod(default), Parsed()));
    }

    [Theory]
    [InlineData(1, true, 0)]
    [InlineData(1, false, 1)]
    [InlineData(9, false, 0)]
    public void Removed_hidden_and_banned_items_all_read_as_gone(int result, bool banned, int visibility)
    {
        var item = new WorkshopItem
        {
            Id = "1", Result = result, Banned = banned, Visibility = visibility,
            TimeUpdatedUnix = 1734773844,
        };

        Assert.True(item.IsUnavailable);
        Assert.Equal(WorkshopState.Gone, WorkshopStatus.For(Mod(DateTime.UtcNow), item));
    }

    [Fact]
    public void The_cache_survives_a_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "workshop.json");

        WorkshopCache.Save(path, new[] { Parsed() });
        var loaded = WorkshopCache.Load(path);

        Assert.NotNull(loaded);
        var item = Assert.Single(loaded!.Value.Items);
        Assert.Equal("Texture Expand", item.Title);
        Assert.Equal(1734773844, item.TimeUpdatedUnix);
        Assert.Equal(new[] { "Utility", "Sprite" }, item.Tags);
    }

    [Fact]
    public void A_corrupt_cache_is_ignored_rather_than_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "workshop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json");

        Assert.Null(WorkshopCache.Load(path));
        Assert.Null(WorkshopCache.Load(Path.Combine(Path.GetTempPath(), "nope", "missing.json")));
    }
}
