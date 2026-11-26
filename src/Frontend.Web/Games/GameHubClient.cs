using Frontend.Domain.DTOs.Games;
using Frontend.Services.Auth;
using Frontend.Services.Options;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace Frontend.Web.Games;

// The game over GameService's SignalR hub (SCRUM-102, 103, 104). Pages use this interface, so
// tests can raise the events with a fake and no server.
// Server errors come out as GameHubException with a Danish message (GameMessages).
public interface IGameHubClient : IAsyncDisposable
{
    // Server -> client.
    event Func<IReadOnlyList<PrefetchDto>, Task>? Prefetch;
    event Func<RoundStartedDto, Task>? RoundStarted;
    event Func<PlayerAnsweredDto, Task>? PlayerAnswered;
    event Func<RoundEndedDto, Task>? RoundEnded;
    event Func<GameEndedDto, Task>? GameEnded;

    // The connection dropped. SignalR tries to reconnect by itself (0, 2, 10 and 30 s).
    event Func<Task>? ConnectionLost;
    // Reconnected: JoinGame has been called again, and this is the game's state now. The server
    // does not resend what was missed, so the page should replace its state with this one.
    event Func<GameStateDto, Task>? Rejoined;

    // Connects (the first time) and joins the game. Throws GameHubException.
    Task<GameStateDto> JoinGameAsync(Guid gameId, CancellationToken ct = default);

    // The file for the round is downloaded and ready to play.
    Task ReadyAsync(Guid gameId, int round, CancellationToken ct = default);

    // SCRUM-168: the server's UTC time, to work out the clock offset (ClockSync). Throws GameHubException.
    Task<DateTimeOffset> ServerTimeAsync(CancellationToken ct = default);

    // Places the round's song at slotIndex in my timeline: 0 = before the first card, n = after the last.
    Task SubmitAnswerAsync(Guid gameId, int round, int slotIndex, CancellationToken ct = default);
}

// Thrown by IGameHubClient. Message is Danish and can be shown to the player as is.
public sealed class GameHubException(string message, Exception? inner = null) : Exception(message, inner);

// Hub: <GameService base>/hubs/game?gameId={gameId}, i.e. /api/gameservice/hubs/game?gameId=... through the edge.
// ADR-0004 / gameservice.gamehub@1.3: every game lives in one GameService pod, and the edge routes the
// connection there by the gameId in the URL. So one connection serves one game: joining another game
// opens a new connection.
// The JWT goes as a Bearer header on negotiate and as ?access_token= on the WebSocket (SignalR does that).
// configureHttp is for tests (e.g. a TestServer handler); DI leaves it null.
public sealed class GameHubClient(
    IOptions<ServiceUrlOptions> serviceUrls,
    IAccessTokenProvider tokens,
    Action<HttpConnectionOptions>? configureHttp = null) : IGameHubClient
{
    private HubConnection? _connection;
    private Guid _gameId;
    private Guid _connectedGameId; // the game in the current connection's URL

    public event Func<IReadOnlyList<PrefetchDto>, Task>? Prefetch;
    public event Func<RoundStartedDto, Task>? RoundStarted;
    public event Func<PlayerAnsweredDto, Task>? PlayerAnswered;
    public event Func<RoundEndedDto, Task>? RoundEnded;
    public event Func<GameEndedDto, Task>? GameEnded;
    public event Func<Task>? ConnectionLost;
    public event Func<GameStateDto, Task>? Rejoined;

    public async Task<GameStateDto> JoinGameAsync(Guid gameId, CancellationToken ct = default)
    {
        _gameId = gameId;
        if (_connection is not null && _connectedGameId != gameId)
        {
            // The new game may live in another GameService pod.
            var old = _connection;
            _connection = null;
            await old.DisposeAsync();
        }
        if (_connection is null)
        {
            var connection = Build(gameId);
            try
            {
                await connection.StartAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await connection.DisposeAsync();
                throw new GameHubException("Kunne ikke forbinde til spillet. Tjek din forbindelse og prøv igen.", e);
            }
            _connection = connection;
            _connectedGameId = gameId;
        }

        return await Call(() => _connection.InvokeAsync<GameStateDto>("JoinGame", gameId, ct));
    }

    public Task ReadyAsync(Guid gameId, int round, CancellationToken ct = default) =>
        Call(() => Connected().InvokeAsync("Ready", gameId, round, ct));

    public Task SubmitAnswerAsync(Guid gameId, int round, int slotIndex, CancellationToken ct = default) =>
        Call(() => Connected().InvokeAsync("SubmitAnswer", gameId, round, slotIndex, ct));

    public Task<DateTimeOffset> ServerTimeAsync(CancellationToken ct = default) =>
        Call(() => Connected().InvokeAsync<DateTimeOffset>("ServerTime", ct));

    private HubConnection Build(Guid gameId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(serviceUrls.Value.GameService, $"hubs/game?gameId={gameId}"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(tokens.GetAccessToken());
                // GameService runs several pods (ADR-0004, games in Redis). Negotiate and the WebSocket after
                // it are two requests that can land on different pods, so skip negotiate and open the
                // WebSocket directly (as LobbyHub, SCRUM-128).
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                configureHttp?.Invoke(options);
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<List<PrefetchDto>>("Prefetch", files => Raise(Prefetch, h => h(files)));
        connection.On<RoundStartedDto>("RoundStarted", round => Raise(RoundStarted, h => h(round)));
        connection.On<PlayerAnsweredDto>("PlayerAnswered", answered => Raise(PlayerAnswered, h => h(answered)));
        connection.On<RoundEndedDto>("RoundEnded", round => Raise(RoundEnded, h => h(round)));
        connection.On<GameEndedDto>("GameEnded", game => Raise(GameEnded, h => h(game)));

        connection.Reconnecting += _ => Raise(ConnectionLost, h => h());
        // Not when we closed it ourselves to switch game (it is no longer _connection then).
        connection.Closed += _ => connection == _connection ? Raise(ConnectionLost, h => h()) : Task.CompletedTask;
        // The server forgets the connection's group on reconnect, so join again.
        connection.Reconnected += async _ =>
        {
            var state = await connection.InvokeAsync<GameStateDto>("JoinGame", _gameId);
            await Raise(Rejoined, h => h(state));
        };
        return connection;
    }

    private HubConnection Connected() =>
        _connection ?? throw new GameHubException("Du er ikke forbundet til spillet.");

    private static async Task Call(Func<Task> call) =>
        await Call(async () => { await call(); return true; });

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (HubException e)
        {
            throw new GameHubException(GameMessages.For(e.Message), e);
        }
        catch (InvalidOperationException e) // e.g. invoked while the connection is reconnecting
        {
            throw new GameHubException("Forbindelsen til spillet er tabt. Vent et øjeblik, så prøver vi igen.", e);
        }
    }

    private static Task Raise<T>(T? handler, Func<T, Task> invoke) where T : Delegate =>
        handler is null ? Task.CompletedTask : invoke(handler);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not { } connection)
            return;
        _connection = null; // the page and DI may both dispose it
        await connection.DisposeAsync();
    }
}
