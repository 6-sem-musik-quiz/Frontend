using Frontend.Domain.DTOs.Lobbies;
using Frontend.Services.Auth;
using Frontend.Services.Options;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace Frontend.Web.Lobbies;

// Live updates for one lobby from LobbyService's SignalR hub (SCRUM-100).
// Pages use this interface, so tests can raise the events without a server.
public interface ILobbyHub : IAsyncDisposable
{
    event Func<LobbyDto, Task>? LobbyUpdated;
    event Func<Guid, Task>? GameStarted;
    event Func<Task>? LobbyClosed;

    // Connects and joins the lobby's group. Throws if the hub cannot be reached or the user is not in the lobby.
    Task ConnectAsync(Guid lobbyId, CancellationToken ct = default);
}

// Hub: <LobbyService base>/hubs/lobby, i.e. /api/lobbyservice/hubs/lobby through the edge.
// The JWT goes as a Bearer header on negotiate and as ?access_token= on the WebSocket (SignalR does that).
// After a reconnect the server has forgotten the group, so it is joined again.
public sealed class LobbyHub(IOptions<ServiceUrlOptions> serviceUrls, IAccessTokenProvider tokens) : ILobbyHub
{
    private HubConnection? _connection;
    private Guid _lobbyId;

    public event Func<LobbyDto, Task>? LobbyUpdated;
    public event Func<Guid, Task>? GameStarted;
    public event Func<Task>? LobbyClosed;

    public async Task ConnectAsync(Guid lobbyId, CancellationToken ct = default)
    {
        _lobbyId = lobbyId;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(serviceUrls.Value.LobbyService, "hubs/lobby"),
                options => options.AccessTokenProvider = () => Task.FromResult(tokens.GetAccessToken()))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<LobbyDto>("LobbyUpdated", lobby => Raise(LobbyUpdated, h => h(lobby)));
        _connection.On<StartGameResponse>("GameStarted", game => Raise(GameStarted, h => h(game.GameId)));
        _connection.On("LobbyClosed", () => Raise(LobbyClosed, h => h()));
        _connection.Reconnected += _ => _connection.InvokeAsync("JoinLobbyGroup", _lobbyId);

        await _connection.StartAsync(ct);
        await _connection.InvokeAsync("JoinLobbyGroup", lobbyId, ct);
    }

    private static Task Raise<T>(T? handler, Func<T, Task> invoke) where T : Delegate =>
        handler is null ? Task.CompletedTask : invoke(handler);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not { } connection)
            return;
        _connection = null; // the page and DI may both dispose it

        try
        {
            if (connection.State == HubConnectionState.Connected)
                await connection.InvokeAsync("LeaveLobbyGroup", _lobbyId);
        }
        catch (Exception)
        {
            // The page is going away; a failed unsubscribe only means the server drops the connection itself.
        }
        await connection.DisposeAsync();
    }
}
