using System.Security.Claims;

using Bunit;

using Frontend.Domain.DTOs.Lobbies;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Pages;
using Frontend.Web.Lobbies;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Frontend.Web.Tests;

// SCRUM-111: /lobby/{id} with a fake hub, so the server's messages can be raised directly.
public class LobbyPageTests : BunitContext
{
    private static readonly Guid LobbyId = Guid.Parse("8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11");
    private const int Me = 1;

    private readonly ILobbyApiClient _lobbies = Substitute.For<ILobbyApiClient>();
    private readonly FakeLobbyHub _hub = new();
    private readonly NavigationManager _navigation;

    public LobbyPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // clipboard
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_lobbies);
        Services.AddSingleton<ILobbyHub>(_hub);
        Services.AddScoped<ApiCallHandler>();
        AddAuthorization().SetAuthorized("alice").SetClaims(new Claim("sub", Me.ToString()));
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private static LobbyDto Lobby(int host, params LobbyPlayerDto[] players) =>
        new(LobbyId, null, host, false, new LobbySettingsDto(3, "100 hits", 30, 10, 4), players, "Waiting");

    private IRenderedComponent<Lobby> RenderPage(LobbyDto lobby)
    {
        _lobbies.GetLobbyAsync(LobbyId, Arg.Any<CancellationToken>()).Returns(lobby);
        return Render<Lobby>(p => p.Add(x => x.Id, LobbyId));
    }

    private static List<string> Players(IRenderedComponent<Lobby> cut) =>
        cut.FindAll("[data-testid=player]").Select(li => li.TextContent.Trim().Replace("\n", " ")).ToList();

    [Fact]
    public void ShowsPlayers_HostCrown_ReadyMark_AndMe()
    {
        var cut = RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", true), new LobbyPlayerDto(Me, "alice", false)));

        var players = cut.FindAll("[data-testid=player]");
        Assert.Contains("👑", players[0].TextContent);
        Assert.NotNull(players[0].QuerySelector("[data-testid=ready]"));
        Assert.Contains("(dig)", players[1].TextContent);
        Assert.Null(players[1].QuerySelector("[data-testid=ready]"));
        Assert.Equal(LobbyId, _hub.ConnectedTo);
    }

    [Fact]
    public void LobbyUpdated_UpdatesThePlayerList()
    {
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)));
        Assert.Single(cut.FindAll("[data-testid=player]"));

        _hub.RaiseLobbyUpdated(Lobby(Me, new LobbyPlayerDto(Me, "alice", false), new LobbyPlayerDto(2, "bob", true)));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=player]").Count));
        Assert.Contains("bob", cut.Markup);
    }

    [Fact]
    public void StartButton_OnlyForTheHost()
    {
        var cut = RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", true), new LobbyPlayerDto(Me, "alice", true)));

        Assert.Empty(cut.FindAll("[data-testid=start-button]"));
    }

    [Fact]
    public void StartButton_DisabledUntilTwoPlayersAndAllReady()
    {
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", true)));
        Assert.True(cut.Find("[data-testid=start-button]").HasAttribute("disabled"));
        Assert.Equal("Venter på 1 spiller mere.", cut.Find("[data-testid=start-reason]").TextContent);

        _hub.RaiseLobbyUpdated(Lobby(Me, new LobbyPlayerDto(Me, "alice", true), new LobbyPlayerDto(2, "bob", false)));
        cut.WaitForAssertion(() => Assert.Equal("Venter på, at 1 spiller bliver klar.", cut.Find("[data-testid=start-reason]").TextContent));
        Assert.True(cut.Find("[data-testid=start-button]").HasAttribute("disabled"));

        _hub.RaiseLobbyUpdated(Lobby(Me, new LobbyPlayerDto(Me, "alice", true), new LobbyPlayerDto(2, "bob", true)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=start-button]").HasAttribute("disabled")));
        Assert.Empty(cut.FindAll("[data-testid=start-reason]"));
    }

    [Fact]
    public void Start_GoesToTheGame()
    {
        var gameId = Guid.NewGuid();
        _lobbies.StartGameAsync(LobbyId, Arg.Any<CancellationToken>()).Returns(new StartGameResponse(gameId));
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", true), new LobbyPlayerDto(2, "bob", true)));

        cut.Find("[data-testid=start-button]").Click();

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/spil/{gameId}", _navigation.Uri));
    }

    [Fact]
    public void GameStarted_SendsEveryoneToTheGame()
    {
        var gameId = Guid.NewGuid();
        RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", true), new LobbyPlayerDto(Me, "alice", true)));

        _hub.RaiseGameStarted(gameId);

        Assert.Equal($"http://localhost/spil/{gameId}", _navigation.Uri);
    }

    [Fact]
    public void LobbyClosed_GoesHomeWithMessage()
    {
        RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", true), new LobbyPlayerDto(Me, "alice", true)));

        _hub.RaiseLobbyClosed();

        Assert.Equal("http://localhost/?lukket=1", _navigation.Uri);
    }

    [Fact]
    public void Ready_TogglesAndShowsTheNewState()
    {
        _lobbies.SetReadyAsync(LobbyId, true, Arg.Any<CancellationToken>())
            .Returns(Lobby(2, new LobbyPlayerDto(2, "bob", false), new LobbyPlayerDto(Me, "alice", true)));
        var cut = RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", false), new LobbyPlayerDto(Me, "alice", false)));
        Assert.Equal("Klar", cut.Find("[data-testid=ready-button]").TextContent.Trim());

        cut.Find("[data-testid=ready-button]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Ikke klar", cut.Find("[data-testid=ready-button]").TextContent.Trim()));
        _lobbies.Received(1).SetReadyAsync(LobbyId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Leave_GoesHome()
    {
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Forlad lobby").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", _navigation.Uri));
        _lobbies.Received(1).LeaveLobbyAsync(LobbyId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PrivateLobby_ShowsTheCode()
    {
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)) with { IsPrivate = true, Code = "ABC234" });

        Assert.Equal("ABC234", cut.Find("[data-testid=code]").TextContent);
    }

    [Fact]
    public void HubUnavailable_ShowsNotLive()
    {
        _hub.FailToConnect = true;

        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)));

        Assert.NotNull(cut.Find("[data-testid=not-live]"));
    }

    [Fact]
    public void UnknownLobby_SaysSo()
    {
        _lobbies.GetLobbyAsync(LobbyId, Arg.Any<CancellationToken>()).Returns<LobbyDto>(_ =>
            throw new Frontend.Services.Errors.ApiException(System.Net.HttpStatusCode.NotFound, "Not found."));

        var cut = Render<Lobby>(p => p.Add(x => x.Id, LobbyId));

        Assert.Contains("findes ikke", cut.Markup);
        Assert.Null(_hub.ConnectedTo);
    }

    // SCRUM-160: a lobby that expired (10 idle minutes) while we were on the page.
    private static Frontend.Services.Errors.ApiException Gone() =>
        new(System.Net.HttpStatusCode.NotFound, "The lobby does not exist.");

    [Fact]
    public void Ready_InAnExpiredLobby_GoesHomeWithMessage()
    {
        _lobbies.SetReadyAsync(LobbyId, true, Arg.Any<CancellationToken>()).Returns<LobbyDto>(_ => throw Gone());
        var cut = RenderPage(Lobby(2, new LobbyPlayerDto(2, "bob", false), new LobbyPlayerDto(Me, "alice", false)));

        cut.Find("[data-testid=ready-button]").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/?lukket=1", _navigation.Uri));
        Assert.DoesNotContain("findes ikke", cut.Markup); // no error over a lobby that no longer exists
    }

    [Fact]
    public void Start_InAnExpiredLobby_GoesHomeWithMessage()
    {
        _lobbies.StartGameAsync(LobbyId, Arg.Any<CancellationToken>()).Returns<StartGameResponse>(_ => throw Gone());
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", true), new LobbyPlayerDto(2, "bob", true)));

        cut.Find("[data-testid=start-button]").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/?lukket=1", _navigation.Uri));
    }

    [Fact]
    public void Leave_AnExpiredLobby_GoesHomeWithMessage()
    {
        _lobbies.LeaveLobbyAsync(LobbyId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(Gone()));
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Forlad lobby").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/?lukket=1", _navigation.Uri));
    }

    [Fact]
    public async Task Polling_FindsTheLobbyGone_GoesHomeWithMessage()
    {
        _hub.FailToConnect = true; // polling every 5 s
        var cut = RenderPage(Lobby(Me, new LobbyPlayerDto(Me, "alice", false)));
        _lobbies.GetLobbyAsync(LobbyId, Arg.Any<CancellationToken>()).Returns<LobbyDto>(_ => throw Gone());

        await Task.Delay(Frontend.Web.Components.Pages.Lobby.PollInterval + TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/?lukket=1", _navigation.Uri));
    }

    private sealed class FakeLobbyHub : ILobbyHub
    {
        public Guid? ConnectedTo { get; private set; }
        public bool FailToConnect { get; set; }

        public event Func<LobbyDto, Task>? LobbyUpdated;
        public event Func<Guid, Task>? GameStarted;
        public event Func<Task>? LobbyClosed;

        public Task ConnectAsync(Guid lobbyId, CancellationToken ct = default)
        {
            if (FailToConnect)
                throw new HttpRequestException("hub down");
            ConnectedTo = lobbyId;
            return Task.CompletedTask;
        }

        public void RaiseLobbyUpdated(LobbyDto lobby) => LobbyUpdated?.Invoke(lobby);
        public void RaiseGameStarted(Guid gameId) => GameStarted?.Invoke(gameId);
        public void RaiseLobbyClosed() => LobbyClosed?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
