using ElinTextureManager.Core.Services;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// How a Workshop link is built. The ID comes from a Workshop folder name, which is
/// untrusted input handed to the shell, so the digits check is a security boundary
/// rather than tidiness.
/// </summary>
public sealed class WorkshopLinkTests
{
    [Fact]
    public void The_steam_client_gets_a_protocol_link()
    {
        Assert.Equal(
            "steam://url/CommunityFilePage/3427045108",
            ShellService.WorkshopLink("3427045108", useSteamClient: true));
    }

    [Fact]
    public void Without_the_client_it_falls_back_to_the_web_page()
    {
        Assert.Equal(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=3427045108",
            ShellService.WorkshopLink("3427045108", useSteamClient: false));
    }

    [Theory]
    [InlineData("342704510a")]
    [InlineData("../../evil")]
    [InlineData("3427 045108")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_plain_id_is_refused(string? id)
    {
        // Nothing arbitrary may reach the shell, by either route.
        Assert.Null(ShellService.WorkshopLink(id, useSteamClient: true));
        Assert.Null(ShellService.WorkshopLink(id, useSteamClient: false));
    }

    [Fact]
    public void A_local_package_has_no_workshop_link()
    {
        // Local mods have no Workshop ID at all; the button is hidden for them.
        Assert.Null(ShellService.WorkshopLink(null, useSteamClient: true));
    }
}
