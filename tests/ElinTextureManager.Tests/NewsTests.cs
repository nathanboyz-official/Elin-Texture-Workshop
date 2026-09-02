using ElinTextureManager.Core.News;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Parsing of Steam's news response. The fetch itself is not tested - it would need the
/// network, and a test that needs the network is a test that fails for the wrong reason.
/// </summary>
public sealed class NewsTests
{
    private const string Sample = """
        {"appnews":{"appid":2135150,"newsitems":[
          {"gid":"1841579228673696","title":"Update 23.338 Stable",
           "url":"https://example.invalid/post","is_external_url":true,"author":"noa",
           "contents":"[h2]Fixes[/h2]\nSomething was fixed.",
           "feedlabel":"Community Announcements","date":1787503042,
           "feedname":"steam_community_announcements","appid":2135150},
          {"gid":"2","title":"Elin State of the Game","url":"https://example.invalid/two",
           "author":"contact","contents":"Greetings.","feedlabel":"Community Announcements",
           "date":1787396771,"appid":2135150}
        ],"count":56}}
        """;

    [Fact]
    public void Announcements_are_parsed()
    {
        var items = SteamNewsClient.Parse(Sample);

        Assert.Equal(2, items.Count);
        Assert.Equal("Update 23.338 Stable", items[0].Title);
        Assert.Equal("noa", items[0].Author);
        Assert.Equal("https://example.invalid/post", items[0].Url);
    }

    [Fact]
    public void The_unix_date_becomes_a_real_date()
    {
        var item = SteamNewsClient.Parse(Sample)[0];

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1787503042),
            item.Date);
    }

    [Fact]
    public void Patch_announcements_are_told_apart_from_general_news()
    {
        var items = SteamNewsClient.Parse(Sample);

        Assert.True(items[0].IsPatchNote);
        Assert.False(items[1].IsPatchNote);
    }

    [Fact]
    public void Steam_markup_is_stripped_from_the_body()
    {
        var item = SteamNewsClient.Parse(Sample)[0];

        Assert.DoesNotContain("[h2]", item.Contents);
        Assert.Contains("Fixes", item.Contents);
        Assert.Contains("Something was fixed.", item.Contents);
    }

    [Fact]
    public void Runs_of_blank_lines_are_collapsed()
    {
        var text = SteamNewsClient.StripBbCode("one\n\n\n\ntwo");

        Assert.Equal("one" + Environment.NewLine + Environment.NewLine + "two", text);
    }

    [Fact]
    public void Malformed_json_is_surfaced_as_a_parse_error()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => SteamNewsClient.Parse("not json"));
    }

    [Fact]
    public void A_response_with_no_items_yields_nothing()
    {
        Assert.Empty(SteamNewsClient.Parse("""{"appnews":{"appid":2135150}}"""));
    }
}
