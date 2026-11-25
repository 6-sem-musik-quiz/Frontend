using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;
using Frontend.Web.Games;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Web.Components.Pages;

public partial class GamePage : IAsyncDisposable
{
    public const string VolumeKey = "slopster.volume";

    // SCRUM-171: a placed but unconfirmed card is sent this long before the time runs out. The server
    // takes answers until EndsAt + 1 s, so the margin covers the network; an auto-locked answer comes
    // in at nearly the full time and earns no speed bonus, which is fair.
    public static readonly TimeSpan AutoLockLead = TimeSpan.FromMilliseconds(300);

    [Parameter] public Guid GameId { get; set; }

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Inject] private IGameHubClient Hub { get; set; } = null!;
    [Inject] private IGameAudio Audio { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    // What the card table (SCRUM-152) knows about a player. Everything here is public.
    private sealed class Player(string name, int score)
    {
        public string Name { get; } = name;
        public int Score { get; set; } = score;
        public List<CardDto> Cards { get; set; } = [];
        public List<bool> History { get; set; } = [];
        public RivalStamp? Stamp { get; set; }     // answered the running round: order and time
        public RivalReveal? Reveal { get; set; }   // between RoundEnded and the next round
    }

    private int? _me;
    private bool _loading = true;
    private GameStateDto? _state;
    private string? _error;
    private bool _connectionLost;

    private List<CardDto> _cards = [];
    private IReadOnlyList<CardDto>? _nextTimeline; // shown when the next round starts, after the reveal
    private readonly List<int> _playerOrder = [];
    private readonly Dictionary<int, Player> _players = [];

    // Songs: round -> the CDN url, and round -> what to play (a blob: URL, or the url if the download failed).
    private readonly Dictionary<int, string> _urls = [];
    private readonly Dictionary<int, string> _sources = [];
    private readonly HashSet<int> _prefetched = [];
    private readonly SemaphoreSlim _prefetchQueue = new(1, 1);

    private int _round;
    private RoundStartedDto? _roundInfo;
    private int? _secondsLeft;
    private bool _timeUp;
    private bool _autoLock;
    private bool _needsPlay;
    private long _playbackStarted;
    private RoundReveal? _reveal;
    private GameEndedDto? _ended;

    // Every ended round's song and how it went for me (discard pile, all songs after the game).
    private readonly List<PlayedSong> _played = [];
    private bool _showAllSongs;
    private double _volume = 0.8;

    private CancellationTokenSource? _roundCts;
    private ITimer? _countdown;
    private ITimer? _autoLockTimer;
    private bool _disposed;

    // SCRUM-168: our clock + this = the server's clock. Null: play on startsInMs instead.
    private TimeSpan? _clockOffset;

    // Lock the placed card just before the time is up (or at the latest when it is). Not when the server
    // already has my answer, e.g. after a reload where the hand no longer knows it was confirmed.
    private bool AutoConfirm =>
        (_autoLock || _timeUp) && !(_me is { } me && _players.TryGetValue(me, out var self) && self.Stamp is not null);

    private string Banner =>
        _reveal is not null ? "Runden er slut"
        : _roundInfo is null ? "Venter på spillerne"
        : _secondsLeft is null ? "Gør dig klar"
        : "Nu spiller";

    private string LockedText => _roundInfo is null
        ? (_round == 0 ? "Spillet starter, når alle er forbundet." : "Næste runde starter om lidt.")
        : "Tiden er gået.";

    // Everyone but me, in lobby order.
    private List<RivalView> Rivals =>
        _playerOrder.Where(id => id != _me)
            .Select(id => (Id: id, P: _players[id]))
            .Select(x => new RivalView(x.Id, x.P.Name, x.P.Cards, x.P.History, x.P.Stamp, x.P.Reveal))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null && int.TryParse((await AuthState).User.FindFirst("sub")?.Value, out var me))
            _me = me;

        Hub.Prefetch += OnPrefetch;
        Hub.RoundStarted += OnRoundStarted;
        Hub.PlayerAnswered += OnPlayerAnswered;
        Hub.RoundEnded += OnRoundEnded;
        Hub.GameEnded += OnGameEnded;
        Hub.ConnectionLost += OnConnectionLost;
        Hub.Rejoined += OnRejoined;

        try
        {
            var state = await Hub.JoinGameAsync(GameId);
            ApplyState(state);
            // Before Ready goes out, so the clock is known when the first round starts.
            _clockOffset = await ClockSync.MeasureAsync(() => Hub.ServerTimeAsync(), Time);
            StartPrefetch(state.Prefetch);
            if (state.CurrentRound is { } running)
                _ = StartRoundAsync(running);
        }
        catch (GameHubException e)
        {
            _error = e.Message;
        }
        _loading = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        try
        {
            var saved = await JS.InvokeAsync<string?>("localStorage.getItem", VolumeKey);
            if (double.TryParse(saved, System.Globalization.CultureInfo.InvariantCulture, out var volume))
                _volume = Math.Clamp(volume, 0, 1);
            await Audio.SetVolumeAsync(_volume);
            StateHasChanged();
        }
        catch (JSException)
        {
            // Storage blocked: the default volume is fine.
        }
    }

    private void ApplyState(GameStateDto state)
    {
        _state = state;
        _round = state.Round;
        _cards = state.MyTimeline.ToList();
        _nextTimeline = null;
        _playerOrder.Clear();
        _players.Clear();
        foreach (var p in state.Players)
        {
            _playerOrder.Add(p.UserId);
            _players[p.UserId] = new Player(p.UserName, p.Score)
            {
                Cards = (p.Timeline ?? (p.UserId == _me ? _cards : [])).ToList(),
                History = (p.History ?? []).OrderBy(h => h.Round).Select(h => h.Correct).ToList(),
                // After a reload the server only knows the order, not the time on our stamp.
                Stamp = p.AnsweredOrder is { } order ? new RivalStamp(order, null) : null
            };
        }

        // After a reload: the past songs, with my own history saying how each went.
        var myHistory = state.Players.FirstOrDefault(p => p.UserId == _me)?.History ?? [];
        _played.Clear();
        foreach (var past in state.PastSongs ?? [])
        {
            var mine = myHistory.FirstOrDefault(h => h.Round == past.Round);
            _played.Add(new PlayedSong(past.Round, past.Song, mine?.Correct == true, mine?.AnsweredAfterMs is not null));
        }
    }

    // ---- Prefetch: download ahead, then tell the server we are ready ----

    private Task OnPrefetch(IReadOnlyList<PrefetchDto> files) => InvokeAsync(() => StartPrefetch(files));

    private void StartPrefetch(IReadOnlyList<PrefetchDto> files)
    {
        foreach (var file in files)
            _urls[file.Round] = file.AudioUrl;
        _ = PrefetchAsync(files.OrderBy(f => f.Round).ToList());
    }

    // One download at a time, in round order, so the next round's song is ready first.
    private async Task PrefetchAsync(List<PrefetchDto> files)
    {
        await _prefetchQueue.WaitAsync();
        try
        {
            foreach (var file in files)
            {
                if (_disposed || !_prefetched.Add(file.Round))
                    continue;
                // Retry once; if the CDN still fails, stream the url directly when the round starts.
                var source = await Audio.PrefetchAsync(file.AudioUrl)
                             ?? await Audio.PrefetchAsync(file.AudioUrl)
                             ?? file.AudioUrl;
                _sources[file.Round] = source;
                try
                {
                    await Hub.ReadyAsync(GameId, file.Round);
                }
                catch (GameHubException)
                {
                    // E.g. the round has already started; the server starts rounds without us after 10 s anyway.
                }
            }
        }
        catch (Exception) when (_disposed)
        {
            // The page is gone.
        }
        finally
        {
            _prefetchQueue.Release();
        }
    }

    // ---- A round: wait startsInMs, play from the offset, count down ----

    private Task OnRoundStarted(RoundStartedDto round) => InvokeAsync(() => { _ = StartRoundAsync(round); });

    private async Task StartRoundAsync(RoundStartedDto round)
    {
        var received = Time.GetTimestamp();
        _roundCts?.Cancel();
        StopCountdown();
        var cts = _roundCts = new CancellationTokenSource();

        if (_nextTimeline is not null)
        {
            _cards = _nextTimeline.ToList();
            _nextTimeline = null;
        }
        _round = round.Round;
        _roundInfo = round;
        _reveal = null;
        _timeUp = false;
        _autoLock = false;
        _needsPlay = false;
        _secondsLeft = null;
        foreach (var p in _players.Values)
        {
            p.Stamp = null;
            p.Reveal = null;
        }
        StateHasChanged();

        // The last round's song played on through its reveal (SCRUM-159): stop it and free the old songs now.
        try
        {
            await Audio.StopAsync();
            foreach (var old in _sources.Keys.Where(r => r < round.Round).ToList())
                await ReleaseAsync(old);
        }
        catch (JSException)
        {
            // Nothing to stop.
        }
        if (cts.IsCancellationRequested)
            return;

        // SCRUM-168: everyone plays at the server's PlaybackStartsAt, whatever their network delay. Without
        // a clock offset (or from an older server), count startsInMs from when the message arrived.
        var wait = round.PlaybackStartsAt is { } at && _clockOffset is { } offset
            ? at - (Time.GetUtcNow() + offset)
            : TimeSpan.FromMilliseconds(round.StartsInMs) - Time.GetElapsedTime(received);
        try
        {
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, Time, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested)
            return;

        // Already late (a slow message, or a rejoin between whole seconds): start at once, further into the
        // song, with that much less time to answer, as the server measures from PlaybackStartsAt.
        var late = wait < TimeSpan.Zero ? -wait : TimeSpan.Zero;
        _playbackStarted = Time.GetTimestamp() - (long)(late.TotalSeconds * Time.TimestampFrequency);
        var remaining = TimeSpan.FromSeconds(round.AnswerTimeSeconds) - late;
        if (remaining <= TimeSpan.Zero)
        {
            _secondsLeft = 0;
            _timeUp = true;
        }
        else
        {
            // E.g. 29.4 s left: show 30 and tick to 29 after 0.4 s, so 0 lands on the server's end of time.
            _secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
            _countdown = Time.CreateTimer(_ => InvokeAsync(Tick), null,
                remaining - TimeSpan.FromSeconds(_secondsLeft.Value - 1), TimeSpan.FromSeconds(1));
            var lockIn = remaining - AutoLockLead;
            _autoLockTimer = Time.CreateTimer(_ => InvokeAsync(AutoLock), null,
                lockIn > TimeSpan.Zero ? lockIn : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        _needsPlay = !await PlayAsync();
        StateHasChanged();
    }

    // Plays the round's song from its offset plus however long playback has been going (so a late
    // "Afspil" click joins the others where they are). False if the browser blocked autoplay.
    private async Task<bool> PlayAsync()
    {
        if (_roundInfo is not { } round)
            return true;
        var source = _sources.GetValueOrDefault(round.Round) ?? _urls.GetValueOrDefault(round.Round);
        if (source is null)
            return true; // nothing downloaded and no url: the round still runs, just silent
        var offset = round.StartOffsetSeconds + Time.GetElapsedTime(_playbackStarted).TotalSeconds;
        try
        {
            return await Audio.PlayAsync(source, offset);
        }
        catch (JSException)
        {
            _error = "Sangen kunne ikke afspilles.";
            return true;
        }
    }

    private async Task PlayClickedAsync()
    {
        _needsPlay = false;
        _needsPlay = !await PlayAsync();
    }

    private void Tick()
    {
        if (_secondsLeft is not > 0)
            return;
        _secondsLeft--;
        if (_secondsLeft == 0)
        {
            // The song keeps playing through the reveal (SCRUM-159); the next round or the game end stops it.
            _timeUp = true;
            StopCountdown();
        }
        StateHasChanged();
    }

    private void AutoLock()
    {
        if (_roundInfo is null || _reveal is not null)
            return;
        _autoLock = true;
        StateHasChanged();
    }

    private void StopCountdown()
    {
        _countdown?.Dispose();
        _countdown = null;
        _autoLockTimer?.Dispose();
        _autoLockTimer = null;
    }

    private async Task ConfirmAsync(int slotIndex)
    {
        try
        {
            // My own stamp arrives with PlayerAnswered like everyone else's.
            await Hub.SubmitAnswerAsync(GameId, _round, slotIndex);
        }
        catch (GameHubException e)
        {
            _error = e.Message;
        }
    }

    // The order counts everyone who has answered this round, me included (the server counts the same way).
    private Task OnPlayerAnswered(PlayerAnsweredDto answered) => InvokeAsync(() =>
    {
        if (_players.TryGetValue(answered.UserId, out var player) && player.Stamp is null)
            player.Stamp = new RivalStamp(_players.Values.Count(p => p.Stamp is not null) + 1, answered.AnsweredAfterMs);
        StateHasChanged();
    });

    // ---- Round end and game end ----

    private Task OnRoundEnded(RoundEndedDto ended) => InvokeAsync(async () =>
    {
        // The song keeps playing while the round is revealed (SCRUM-159), so it is neither stopped
        // nor freed here: the next round does that.
        _roundCts?.Cancel();
        StopCountdown();
        // The round is over: no time left, also when everyone answered early (the timer would otherwise
        // stay frozen on the seconds that were left through the whole reveal).
        _secondsLeft = 0;

        var mine = ended.Results.FirstOrDefault(r => r.UserId == _me);
        _reveal = new RoundReveal(ended.Song, Answered: mine?.SlotIndex is not null, Correct: mine?.Correct == true);
        // The hand keeps showing the reveal; the new timeline replaces it when the next round starts.
        _nextTimeline = mine?.Timeline;
        _played.RemoveAll(p => p.Round == ended.Round);
        _played.Add(new PlayedSong(ended.Round, ended.Song, mine?.Correct == true, mine?.SlotIndex is not null));
        foreach (var result in ended.Results)
        {
            if (_players.TryGetValue(result.UserId, out var player))
            {
                player.Score = result.Score;
                player.Cards = result.Timeline.ToList();
                player.History.Add(result.Correct);
                player.Reveal = new RivalReveal(ended.Song, result.Correct, result.SlotIndex);
            }
        }
        StateHasChanged();
    });

    private Task OnGameEnded(GameEndedDto ended) => InvokeAsync(async () =>
    {
        _roundCts?.Cancel();
        StopCountdown();
        _ended = ended;
        await Audio.StopAsync();
        foreach (var round in _sources.Keys.ToList())
            await ReleaseAsync(round);
        StateHasChanged();
    });

    private async Task ReleaseAsync(int round)
    {
        if (_sources.Remove(round, out var source) && source.StartsWith("blob:", StringComparison.Ordinal))
            await Audio.RevokeAsync(source);
    }

    // ---- Connection ----

    private Task OnConnectionLost() => InvokeAsync(() =>
    {
        _connectionLost = true;
        StateHasChanged();
    });

    // After a reconnect the server does not resend what was missed: start over from its state.
    private Task OnRejoined(GameStateDto state) => InvokeAsync(async () =>
    {
        _connectionLost = false;
        _error = null;
        ApplyState(state);
        // The new connection may take another route: measure again, keeping the old offset if that fails.
        _clockOffset = await ClockSync.MeasureAsync(() => Hub.ServerTimeAsync(), Time) ?? _clockOffset;
        StartPrefetch(state.Prefetch);
        if (state.CurrentRound is { } running)
            _ = StartRoundAsync(running);
        StateHasChanged();
    });

    // ---- Volume ----

    private async Task OnVolumeAsync(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var percent))
            return;
        _volume = Math.Clamp(percent / 100, 0, 1);
        await Audio.SetVolumeAsync(_volume);
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", VolumeKey,
                _volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (JSException)
        {
            // Storage blocked: the volume still applies for this visit.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _roundCts?.Cancel();
        StopCountdown();
        Hub.Prefetch -= OnPrefetch;
        Hub.RoundStarted -= OnRoundStarted;
        Hub.PlayerAnswered -= OnPlayerAnswered;
        Hub.RoundEnded -= OnRoundEnded;
        Hub.GameEnded -= OnGameEnded;
        Hub.ConnectionLost -= OnConnectionLost;
        Hub.Rejoined -= OnRejoined;
        try
        {
            await Audio.StopAsync();
            foreach (var round in _sources.Keys.ToList())
                await ReleaseAsync(round);
        }
        catch (JSDisconnectedException)
        {
            // The browser tab is gone.
        }
        await Hub.DisposeAsync();
    }
}
