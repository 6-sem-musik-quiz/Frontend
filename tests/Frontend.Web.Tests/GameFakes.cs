using Frontend.Domain.DTOs.Games;
using Frontend.Web.Games;

namespace Frontend.Web.Tests;

// A hub without a server: tests raise the server's messages and read what the page sent.
public sealed class FakeGameHub : IGameHubClient
{
    public event Func<IReadOnlyList<PrefetchDto>, Task>? Prefetch;
    public event Func<RoundStartedDto, Task>? RoundStarted;
    public event Func<PlayerAnsweredDto, Task>? PlayerAnswered;
    public event Func<RoundEndedDto, Task>? RoundEnded;
    public event Func<GameEndedDto, Task>? GameEnded;
    public event Func<Task>? ConnectionLost;
    public event Func<GameStateDto, Task>? Rejoined;

    public GameStateDto? State { get; set; }
    public GameHubException? JoinError { get; set; }
    public List<int> Ready { get; } = [];
    public List<(int Round, int Slot)> Answers { get; } = [];
    public bool Disposed { get; private set; }

    public Task<GameStateDto> JoinGameAsync(Guid gameId, CancellationToken ct = default) =>
        JoinError is not null ? Task.FromException<GameStateDto>(JoinError) : Task.FromResult(State!);

    public Task ReadyAsync(Guid gameId, int round, CancellationToken ct = default)
    {
        Ready.Add(round);
        return Task.CompletedTask;
    }

    public Task SubmitAnswerAsync(Guid gameId, int round, int slotIndex, CancellationToken ct = default)
    {
        Answers.Add((round, slotIndex));
        return Task.CompletedTask;
    }

    public Task RaisePrefetch(params PrefetchDto[] files) => Prefetch?.Invoke(files) ?? Task.CompletedTask;
    public Task RaiseRoundStarted(RoundStartedDto round) => RoundStarted?.Invoke(round) ?? Task.CompletedTask;
    public Task RaisePlayerAnswered(int userId, int afterMs = 0) =>
        PlayerAnswered?.Invoke(new PlayerAnsweredDto(userId, afterMs)) ?? Task.CompletedTask;
    public Task RaiseRoundEnded(RoundEndedDto round) => RoundEnded?.Invoke(round) ?? Task.CompletedTask;
    public Task RaiseGameEnded(GameEndedDto game) => GameEnded?.Invoke(game) ?? Task.CompletedTask;
    public Task RaiseConnectionLost() => ConnectionLost?.Invoke() ?? Task.CompletedTask;
    public Task RaiseRejoined(GameStateDto state) => Rejoined?.Invoke(state) ?? Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

// Audio without a browser: downloads "succeed" as blob:<url> unless told to fail.
public sealed class FakeGameAudio : IGameAudio
{
    public Dictionary<string, int> FailuresLeft { get; } = [];
    public List<string> Prefetched { get; } = [];
    public List<(string Source, double Offset)> Played { get; } = [];
    public List<string> Revoked { get; } = [];
    public List<double> Volumes { get; } = [];
    public int Stops { get; private set; }
    public bool Playing { get; private set; } // between a PlayAsync that was not blocked and the next StopAsync
    public bool BlockAutoplay { get; set; }

    public Task<string?> PrefetchAsync(string url)
    {
        Prefetched.Add(url);
        if (FailuresLeft.TryGetValue(url, out var left) && left > 0)
        {
            FailuresLeft[url] = left - 1;
            return Task.FromResult<string?>(null);
        }
        return Task.FromResult<string?>("blob:" + url);
    }

    public Task<bool> PlayAsync(string source, double offsetSeconds)
    {
        Played.Add((source, offsetSeconds));
        Playing = !BlockAutoplay;
        return Task.FromResult(!BlockAutoplay);
    }

    public Task StopAsync()
    {
        Stops++;
        Playing = false;
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string blobUrl)
    {
        Revoked.Add(blobUrl);
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        Volumes.Add(volume);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
