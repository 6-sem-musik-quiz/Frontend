using Bunit;

using Frontend.Domain.DTOs.Lobbies;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Lobbies;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Frontend.Web.Tests;

// A player who left the lobby page without leaving can get back to the lobby, or leave it.
public class CurrentLobbyBannerTests : BunitContext
{
    private static readonly LobbyDto MyLobby = new(Guid.Parse("8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11"), null, 2, false,
        new LobbySettingsDto(3, "100 hits", 30, 10, 4), [new LobbyPlayerDto(2, "mathias", false)], "Waiting");

    private readonly ILobbyApiClient _lobbies = Substitute.For<ILobbyApiClient>();

    public CurrentLobbyBannerTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_lobbies);
        Services.AddScoped<ApiCallHandler>();
    }

    [Fact]
    public void InALobby_ShowsItWithGoToAndLeave()
    {
        _lobbies.GetMyLobbyAsync(Arg.Any<CancellationToken>()).Returns(MyLobby);

        var cut = Render<CurrentLobbyBanner>();

        var banner = cut.Find("[data-testid=current-lobby]");
        Assert.Contains("100 hits", banner.TextContent);
        Assert.Contains("1/4", banner.TextContent);
        Assert.Equal($"lobby/{MyLobby.Id}", banner.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void NotInALobby_ShowsNothing()
    {
        _lobbies.GetMyLobbyAsync(Arg.Any<CancellationToken>()).Returns((LobbyDto?)null);

        var cut = Render<CurrentLobbyBanner>();

        Assert.Empty(cut.FindAll("[data-testid=current-lobby]"));
    }

    [Fact]
    public void Leave_LeavesTheLobbyAndHidesTheBanner()
    {
        _lobbies.GetMyLobbyAsync(Arg.Any<CancellationToken>()).Returns(MyLobby);
        var cut = Render<CurrentLobbyBanner>();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Forlad lobby").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=current-lobby]")));
        _lobbies.Received(1).LeaveLobbyAsync(MyLobby.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LookupFails_ShowsNothingAndNoError()
    {
        _lobbies.GetMyLobbyAsync(Arg.Any<CancellationToken>()).Returns<LobbyDto?>(_ => throw new HttpRequestException("down"));

        var cut = Render<CurrentLobbyBanner>();

        Assert.Empty(cut.FindAll("[data-testid=current-lobby]"));
        Assert.Empty(cut.FindAll(".alert-danger"));
    }
}
