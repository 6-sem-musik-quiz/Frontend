using System.Net;
using System.Net.Http.Json;

using Frontend.Domain.DTOs.Lobbies;
using Frontend.Services.ApiClients;

namespace Frontend.Services.Tests;

// Routes, methods and bodies against LobbyService (SCRUM-96 to 99), relative to /api/lobbyservice/.
public class LobbyApiClientTests
{
    private static readonly Guid LobbyId = Guid.Parse("8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11");

    private static readonly LobbyDto Lobby = new(LobbyId, null, 1, false,
        new LobbySettingsDto(3, "80s", 30, 10, 4), [new LobbyPlayerDto(1, "alice", false)], "Waiting");

    private static (LobbyApiClient Client, StubHttpMessageHandler Stub) Create(HttpStatusCode status, object? body = null)
    {
        var stub = new StubHttpMessageHandler(status, body);
        return (new LobbyApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/lobbyservice/") }), stub);
    }

    [Fact]
    public async Task GetLobbies_ReadsSummaries()
    {
        var (client, stub) = Create(HttpStatusCode.OK, new[] { new LobbySummaryDto(LobbyId, "alice", "80s", 2, 4, 10, 30) });

        var lobbies = await client.GetLobbiesAsync();

        Assert.Equal("/api/lobbyservice/lobbies", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("alice", lobbies.Single().HostUserName);
    }

    [Fact]
    public async Task Create_PostsSettings()
    {
        var (client, stub) = Create(HttpStatusCode.Created, Lobby);

        await client.CreateLobbyAsync(new CreateLobbyRequest(3, 30, 10, 4, true));

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal("/api/lobbyservice/lobbies", stub.LastRequest.RequestUri!.AbsolutePath);
        var body = await stub.LastRequest.Content!.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(["categoryId", "answerTimeSeconds", "rounds", "maxPlayers", "isPrivate"], body!.Keys);
    }

    [Fact]
    public async Task Join_JoinByCode_Leave_Ready_Start_UseTheLobbyRoutes()
    {
        var (client, stub) = Create(HttpStatusCode.OK, Lobby);
        await client.JoinLobbyAsync(LobbyId);
        Assert.Equal($"/api/lobbyservice/lobbies/{LobbyId}/join", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, stub.LastRequest.Method);

        (client, stub) = Create(HttpStatusCode.OK, Lobby);
        await client.JoinByCodeAsync("ABC234");
        Assert.Equal("/api/lobbyservice/lobbies/join-by-code", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(new JoinByCodeRequest("ABC234"), await stub.LastRequest.Content!.ReadFromJsonAsync<JoinByCodeRequest>());

        (client, stub) = Create(HttpStatusCode.NoContent);
        await client.LeaveLobbyAsync(LobbyId);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal($"/api/lobbyservice/lobbies/{LobbyId}/leave", stub.LastRequest.RequestUri!.AbsolutePath);

        (client, stub) = Create(HttpStatusCode.OK, Lobby);
        await client.SetReadyAsync(LobbyId, true);
        Assert.Equal($"/api/lobbyservice/lobbies/{LobbyId}/ready", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(new ReadyRequest(true), await stub.LastRequest.Content!.ReadFromJsonAsync<ReadyRequest>());

        var gameId = Guid.NewGuid();
        (client, stub) = Create(HttpStatusCode.OK, new StartGameResponse(gameId));
        var started = await client.StartGameAsync(LobbyId);
        Assert.Equal($"/api/lobbyservice/lobbies/{LobbyId}/start", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(gameId, started.GameId);
    }

    [Fact]
    public async Task LobbyDto_ReadsStatusAsString()
    {
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"id":"{{LobbyId}}","code":"ABC234","hostUserId":1,"isPrivate":true,"settings":{"categoryId":3,"categoryName":"80s","answerTimeSeconds":30,"rounds":10,"maxPlayers":4},"players":[{"userId":1,"userName":"alice","isReady":true}],"status":"InGame"}""",
                System.Text.Encoding.UTF8, "application/json")
        });
        var client = new LobbyApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/lobbyservice/") });

        var lobby = await client.GetLobbyAsync(LobbyId);

        Assert.Equal("InGame", lobby.Status);
        Assert.Equal("ABC234", lobby.Code);
        Assert.True(lobby.Players[0].IsReady);
    }

    [Fact]
    public async Task GetMyLobby_ReturnsTheLobby()
    {
        var (client, stub) = Create(HttpStatusCode.OK, Lobby);

        var lobby = await client.GetMyLobbyAsync();

        Assert.Equal("/api/lobbyservice/lobbies/mine", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(LobbyId, lobby?.Id);
    }

    [Fact]
    public async Task GetMyLobby_404_IsNull()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.NotFound,
            """{"title":"Not found.","status":404,"detail":"You are not in a lobby."}""");
        var client = new LobbyApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/lobbyservice/") });

        Assert.Null(await client.GetMyLobbyAsync());
    }
}
