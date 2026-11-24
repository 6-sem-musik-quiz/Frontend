using System.Collections.Concurrent;

using Frontend.Domain.DTOs.Games;
using Frontend.Services.Auth;
using Frontend.Services.Options;
using Frontend.Web.Games;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace Frontend.Web.Tests;

// SCRUM-147: GameHubClient against an in-process SignalR server that behaves like GameService's
// /hubs/game. The server sends anonymous objects with GameService's camelCase JSON, not the
// frontend's own records, so a field that does not match the contract fails here.
public class GameHubClientTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid GameId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    // What the fake server received.
    public sealed class Calls
    {
        public ConcurrentQueue<string> Received { get; } = new();
        public ConcurrentQueue<string?> Authorization { get; } = new();
    }

    // Answers like GameService (SCRUM-102/103/104): JoinGame returns a state, unknown games and
    // double answers throw HubException with GameService's English texts.
    public sealed class FakeGameHub(Calls calls) : Hub
    {
        public object JoinGame(Guid gameId)
        {
            calls.Authorization.Enqueue(Context.GetHttpContext()?.Request.Headers.Authorization.ToString());
            calls.Received.Enqueue($"JoinGame {gameId}");
            if (gameId != GameId)
                throw new HubException("The game does not exist.");
            return new
            {
                gameId,
                settings = new { categoryId = 3, categoryName = "80'er hits", rounds = 10, answerTimeSeconds = 30 },
                round = 0,
                totalRounds = 10,
                players = new object[]
                {
                    new { userId = 7, userName = "asger", score = 1, timeline = new[] { new { title = "Dancing Queen", artists = new[] { "ABBA" }, year = 1976 } },
                          history = new[] { new { round = 1, correct = true, answeredAfterMs = (int?)3200 } }, answeredOrder = (int?)1 },
                    new { userId = 8, userName = "mathias", score = 0, timeline = Array.Empty<object>(),
                          history = new[] { new { round = 1, correct = false, answeredAfterMs = (int?)null } }, answeredOrder = (int?)null }
                },
                myTimeline = new[] { new { title = "Dancing Queen", artists = new[] { "ABBA" }, year = 1976 } },
                prefetch = new[] { new { round = 1, audioUrl = "https://media.munkcloud.dk/songs/1.mp3" } },
                currentRound = (object?)null,
                pastSongs = new[] { new { round = 1, song = new { title = "Take On Me", artists = new[] { "a-ha" }, year = 1985 } } }
            };
        }

        public void Ready(Guid gameId, int round) => calls.Received.Enqueue($"Ready {gameId} {round}");

        public void SubmitAnswer(Guid gameId, int round, int slotIndex)
        {
            if (slotIndex == 99) throw new HubException("You have already answered.");
            calls.Received.Enqueue($"SubmitAnswer {gameId} {round} {slotIndex}");
        }
    }

    private readonly Calls _calls = new();
    private WebApplication _app = null!;
    private TestServer _server = null!;
    private readonly List<GameHubClient> _clients = [];

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(_calls);
        _app = builder.Build();
        _app.MapHub<FakeGameHub>("/api/gameservice/hubs/game");
        await _app.StartAsync();
        _server = _app.GetTestServer();
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
        await _app.DisposeAsync();
    }

    private GameHubClient Client(string gameServiceBase = "http://localhost/api/gameservice/")
    {
        var urls = Options.Create(new ServiceUrlOptions
        {
            UserService = new Uri("http://localhost/api/userservice/"),
            MusicService = new Uri("http://localhost/api/musicservice/"),
            StatisticService = new Uri("http://localhost/api/statisticservice/"),
            LobbyService = new Uri("http://localhost/api/lobbyservice/"),
            GameService = new Uri(gameServiceBase)
        });
        var tokens = Substitute.For<IAccessTokenProvider>();
        tokens.GetAccessToken().Returns("the-jwt");
        var client = new GameHubClient(urls, tokens, options =>
        {
            options.HttpMessageHandlerFactory = _ => _server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
        });
        _clients.Add(client);
        return client;
    }

    private IClientProxy Everyone => _app.Services.GetRequiredService<IHubContext<FakeGameHub>>().Clients.All;

    private static TaskCompletionSource<T> Capture<T>(out Func<T, Task> handler)
    {
        var received = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler = value => { received.TrySetResult(value); return Task.CompletedTask; };
        return received;
    }

    [Fact]
    public async Task JoinGame_SendsTheToken_AndReturnsTheGameState()
    {
        var client = Client();

        var state = await client.JoinGameAsync(GameId);

        Assert.Contains("Bearer the-jwt", _calls.Authorization);
        Assert.Equal(GameId, state.GameId);
        Assert.Equal(new GameSettingsDto(3, "80'er hits", 10, 30), state.Settings);
        Assert.Equal((0, 10), (state.Round, state.TotalRounds));
        Assert.Equal(["asger", "mathias"], state.Players.Select(p => p.UserName));
        var card = Assert.Single(state.MyTimeline);
        Assert.Equal(("Dancing Queen", 1976), (card.Title, card.Year));
        Assert.Equal(["ABBA"], card.Artists);
        Assert.Equal(new PrefetchDto(1, "https://media.munkcloud.dk/songs/1.mp3"), Assert.Single(state.Prefetch));
        Assert.Null(state.CurrentRound);
        // SCRUM-150 fields
        var asger = state.Players[0];
        Assert.Equal("Dancing Queen", Assert.Single(asger.Timeline!).Title);
        Assert.Equal(new RoundHistoryDto(1, true, 3200), Assert.Single(asger.History!));
        Assert.Equal(1, asger.AnsweredOrder);
        Assert.Null(state.Players[1].AnsweredOrder);
        Assert.Null(state.Players[1].History![0].AnsweredAfterMs);
        Assert.Equal((1, "Take On Me"), (state.PastSongs![0].Round, state.PastSongs[0].Song.Title));
    }

    [Fact]
    public async Task ServerMessages_RaiseTheEvents_WithGameServicesJson()
    {
        var client = Client();
        var prefetch = Capture<IReadOnlyList<PrefetchDto>>(out var onPrefetch);
        var started = Capture<RoundStartedDto>(out var onStarted);
        var answered = Capture<PlayerAnsweredDto>(out var onAnswered);
        var ended = Capture<RoundEndedDto>(out var onEnded);
        var gameEnded = Capture<GameEndedDto>(out var onGameEnded);
        client.Prefetch += onPrefetch;
        client.RoundStarted += onStarted;
        client.PlayerAnswered += onAnswered;
        client.RoundEnded += onEnded;
        client.GameEnded += onGameEnded;
        await client.JoinGameAsync(GameId);

        await Everyone.SendAsync("Prefetch", new[]
        {
            new { round = 1, audioUrl = "https://media.munkcloud.dk/songs/1.mp3" },
            new { round = 2, audioUrl = "https://media.munkcloud.dk/songs/2.mp3" }
        });
        await Everyone.SendAsync("RoundStarted",
            new { round = 1, totalRounds = 10, startsInMs = 3000, startOffsetSeconds = 42, answerTimeSeconds = 30 });
        await Everyone.SendAsync("PlayerAnswered", new { userId = 8, answeredAfterMs = 4200 });
        var timeline = new[]
        {
            new { title = "Dancing Queen", artists = new[] { "ABBA" }, year = 1976 },
            new { title = "Take On Me", artists = new[] { "a-ha" }, year = 1985 }
        };
        await Everyone.SendAsync("RoundEnded", new
        {
            round = 1,
            song = new { title = "Take On Me", artists = new[] { "a-ha" }, year = 1985 },
            results = new object[]
            {
                new { userId = 7, userName = "asger", slotIndex = (int?)1, correct = true, score = 1, timeline, answeredAfterMs = (int?)2500, range = new int?[] { 1976, null } },
                new { userId = 8, userName = "mathias", slotIndex = (int?)null, correct = false, score = 0, timeline = timeline[..1], answeredAfterMs = (int?)null, range = (int?[]?)null }
            }
        });
        await Everyone.SendAsync("GameEnded", new
        {
            gameId = GameId,
            standings = new[]
            {
                new { userId = 7, userName = "asger", score = 1, correct = 1, answered = 1, placement = 1, timeline },
                new { userId = 8, userName = "mathias", score = 0, correct = 0, answered = 0, placement = 2, timeline = timeline[..1] }
            }
        });

        Assert.Equal([1, 2], (await prefetch.Task.WaitAsync(Timeout)).Select(p => p.Round));
        Assert.Equal(new RoundStartedDto(1, 10, 3000, 42, 30), await started.Task.WaitAsync(Timeout));
        Assert.Equal(new PlayerAnsweredDto(8, 4200), await answered.Task.WaitAsync(Timeout));

        var round = await ended.Task.WaitAsync(Timeout);
        Assert.Equal(("Take On Me", 1985), (round.Song.Title, round.Song.Year));
        Assert.Equal((1, true, 1, 2), (round.Results[0].SlotIndex, round.Results[0].Correct, round.Results[0].Score, round.Results[0].Timeline.Count));
        Assert.Null(round.Results[1].SlotIndex);
        Assert.Equal(2500, round.Results[0].AnsweredAfterMs);
        Assert.Equal([1976, null], round.Results[0].Range!);
        Assert.Null(round.Results[1].Range);

        var final = await gameEnded.Task.WaitAsync(Timeout);
        Assert.Equal(GameId, final.GameId);
        Assert.Equal([(7, 1, 1, 1, 1), (8, 0, 0, 0, 2)],
            final.Standings.Select(s => (s.UserId, s.Score, s.Correct, s.Answered, s.Placement)));
    }

    [Fact]
    public async Task Ready_AndSubmitAnswer_CallTheHubWithTheRightArguments()
    {
        var client = Client();
        await client.JoinGameAsync(GameId);

        await client.ReadyAsync(GameId, 3);
        await client.SubmitAnswerAsync(GameId, 3, 2);

        Assert.Equal([$"JoinGame {GameId}", $"Ready {GameId} 3", $"SubmitAnswer {GameId} 3 2"], _calls.Received);
    }

    [Fact]
    public async Task ServerErrors_BecomeDanishMessages()
    {
        var client = Client();

        var unknown = await Assert.ThrowsAsync<GameHubException>(() => client.JoinGameAsync(Guid.NewGuid()));
        Assert.Equal("Spillet findes ikke (længere).", unknown.Message);

        await client.JoinGameAsync(GameId);
        var twice = await Assert.ThrowsAsync<GameHubException>(() => client.SubmitAnswerAsync(GameId, 1, 99));
        Assert.Equal("Du har allerede svaret.", twice.Message);
    }

    [Fact]
    public async Task UnreachableServer_GivesAReadableError()
    {
        var client = Client("http://localhost/api/nothing-here/");

        var error = await Assert.ThrowsAsync<GameHubException>(() => client.JoinGameAsync(GameId));

        Assert.StartsWith("Kunne ikke forbinde til spillet", error.Message);
    }

    [Fact]
    public async Task Calls_BeforeJoining_GiveAReadableError()
    {
        var error = await Assert.ThrowsAsync<GameHubException>(() => Client().ReadyAsync(GameId, 1));

        Assert.Equal("Du er ikke forbundet til spillet.", error.Message);
    }
}
