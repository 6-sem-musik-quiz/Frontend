using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;
using Frontend.Web.Games;
using Frontend.Web.Sound;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Web.Components.Pages;

public partial class GamePage : IAsyncDisposable
{
    public const string VolumeKey = "slopster.volume";

    [Parameter] public Guid GameId { get; set; }

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Inject] private IGameHubClient Hub { get; set; } = null!;
    [Inject] private IGameAudio Audio { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ISoundEffects Sounds { get; set; } = null!;

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
    private bool _disposed;

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
        _needsPlay = false;
        _secondsLeft = null;
        foreach (var p in _players.Values)
        {
            p.Stamp = null;
            p.Reveal = null;
        }
        StateHasChanged();

        try
        {
            // Relative timer only: the device clock is never used, so a wrong clock does not matter.
            if (round.StartsInMs > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(round.StartsInMs), Time, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested)
            return;

        await Sounds.PlayAsync(Sfx.RoundStart); // the cards are fanned out as the song begins
        _playbackStarted = Time.GetTimestamp();
        _secondsLeft = round.AnswerTimeSeconds;
        _countdown = Time.CreateTimer(_ => InvokeAsync(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
        if (_secondsLeft is > 0 and <= 5)
            _ = Sounds.PlayAsync(Sfx.Tick);
        if (_secondsLeft == 0)
        {
            _ = Sounds.PlayAsync(Sfx.TimeUp);
            _timeUp = true;
            StopCountdown();
            _ = Audio.StopAsync();
        }
        StateHasChanged();
    }

    private void StopCountdown()
    {
        _countdown?.Dispose();
        _countdown = null;
    }

    private async Task ConfirmAsync(int slotIndex)
    {
        try
        {
            // My own stamp arrives with PlayerAnswered like everyone else's.
            await Hub.SubmitAnswerAsync(GameId, _round, slotIndex);
            await Sounds.PlayAsync(Sfx.LockIn); // a chip on the table: the answer is locked
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
        _roundCts?.Cancel();
        StopCountdown();
        await Audio.StopAsync();

        var mine = ended.Results.FirstOrDefault(r => r.UserId == _me);
        _reveal = new RoundReveal(ended.Song, Answered: mine?.SlotIndex is not null, Correct: mine?.Correct == true);
        await Sounds.PlayAsync(_reveal.Correct ? Sfx.Correct : Sfx.Wrong);
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
        await ReleaseAsync(ended.Round);
        StateHasChanged();
    });

    private Task OnGameEnded(GameEndedDto ended) => InvokeAsync(async () =>
    {
        _roundCts?.Cancel();
        StopCountdown();
        _ended = ended;
        await Audio.StopAsync();
        var me = ended.Standings.FirstOrDefault(s => s.UserId == _me);
        await Sounds.PlayAsync(me?.Placement == 1 ? Sfx.Winner : Sfx.GameOver);
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
    private Task OnRejoined(GameStateDto state) => InvokeAsync(() =>
    {
        _connectionLost = false;
        _error = null;
        ApplyState(state);
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
