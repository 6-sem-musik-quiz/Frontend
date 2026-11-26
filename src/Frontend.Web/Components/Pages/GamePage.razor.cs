using System.Net;

using Frontend.Domain.DTOs.Games;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Components.Game;
using Frontend.Web.Games;
using Frontend.Web.Lobbies;
using Frontend.Web.Sound;

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

    // SCRUM-169: how long "Nu!" stays up after the countdown, when the song has started.
    public static readonly TimeSpan GoShownFor = TimeSpan.FromMilliseconds(800);

    [Parameter] public Guid GameId { get; set; }

    // SCRUM-172: the lobby the game was started from (?lobby=), so "Spil igen" can lead back to it.
    // Without it (an old link) the end screen only offers the front page.
    [SupplyParameterFromQuery(Name = "lobby")] public Guid? LobbyId { get; set; }

    public static string Link(Guid gameId, Guid lobbyId) => $"spil/{gameId}?lobby={lobbyId}";

    // LobbyService reopens the lobby when GameService's GameFinished reaches it, usually well under a
    // second after GameEnded. Until then play-again answers 409, so it is tried again for a while.
    public static readonly TimeSpan PlayAgainRetryDelay = TimeSpan.FromMilliseconds(500);
    public const int PlayAgainAttempts = 20;
    private const string NotFinishedYet = "The game has not finished yet.";

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    [Inject] private IGameHubClient Hub { get; set; } = null!;
    [Inject] private IGameAudio Audio { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ISoundEffects Sounds { get; set; } = null!;
    [Inject] private ILobbyApiClient Lobbies { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

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
    private bool _answeredThisRound; // set when I send my answer (by "Bekræft" or the auto-lock)
    private bool _needsPlay;
    private long _playbackStarted;
    private RoundReveal? _reveal;
    private RoundEndedDto? _roundEnded; // SCRUM-154: the last round's answers, for the guess map and lanes
    private GameEndedDto? _ended;

    // Every ended round's song and how it went for me (discard pile, all songs after the game).
    private readonly List<PlayedSong> _played = [];
    private bool _showAllSongs;
    private bool _playAgainBusy;
    private string? _playAgainError;
    private double _volume = 0.8;
    private double _effectsVolume = 0.6; // sound effects, separate from the song (SCRUM-161)

    private CancellationTokenSource? _roundCts;
    private ITimer? _countdown;
    private ITimer? _autoLockTimer;

    // SCRUM-169: the countdown to the song. _startsIn is 3, 2, 1 while it runs (the hand is locked then),
    // _go shows "Nu!" for a moment when the song starts.
    private int? _startsIn;
    private bool _go;
    private ITimer? _preRollTimer;
    private ITimer? _goTimer;
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
        : _startsIn is not null ? "Sangen starter om lidt."
        : "Tiden er gået.";

    // The hand can only be used while the song plays: not before the countdown is over, not after the time.
    private bool HandLocked => _timeUp || _roundInfo is null || _startsIn is not null;

    // SCRUM-169: where I stand, on the countdown screen from round 2 on (in round 1 everyone has 0).
    private string? Standing =>
        _round > 1 && _me is { } me && _players.TryGetValue(me, out var self)
            ? $"Du har {self.Score} point · nr. {1 + _players.Values.Count(p => p.Score > self.Score)} af {_players.Count}"
            : null;

    // Everyone but me, in lobby order.
    private List<RivalView> Rivals =>
        _playerOrder.Where(id => id != _me)
            .Select(id => (Id: id, P: _players[id]))
            .Select(x => new RivalView(x.Id, x.P.Name, x.P.Cards, x.P.History, x.P.Stamp, x.P.Reveal))
            .ToList();

    // SCRUM-172: "Spil igen". Back to the lobby page, where the player is not ready yet.
    private async Task PlayAgainAsync()
    {
        if (LobbyId is not { } lobbyId || _playAgainBusy) return;
        _playAgainBusy = true;
        _playAgainError = null;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await Lobbies.PlayAgainAsync(lobbyId);
                    Navigation.NavigateTo($"lobby/{lobbyId}");
                    return;
                }
                catch (ApiException e) when (e.StatusCode == HttpStatusCode.Conflict
                                             && e.Problem?.Detail == NotFinishedYet && attempt < PlayAgainAttempts)
                {
                    await Task.Delay(PlayAgainRetryDelay, Time);
                }
            }
        }
        catch (ApiException e) when (e.Problem?.Detail == NotFinishedYet)
        {
            _playAgainError = "Lobbyen er ikke klar endnu. Prøv igen om lidt.";
        }
        catch (ApiException e)
        {
            _playAgainError = LobbyMessages.For(e) ?? "Du kunne ikke komme tilbage til lobbyen.";
        }
        catch (HttpRequestException)
        {
            _playAgainError = "Kunne ikke kontakte lobbyen. Prøv igen om lidt.";
        }
        _playAgainBusy = false;
    }

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
            _effectsVolume = await Sounds.GetVolumeAsync();
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
        _answeredThisRound = false;
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
        // SCRUM-169: count down to the same instant the song starts, so it hits zero for everyone together.
        // A player who (re)joins in the middle of it sees what is left.
        if (wait > TimeSpan.Zero)
            StartPreRoll(wait);
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
        if (_startsIn is not null)
            ShowGo();

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
        // Sounds (SCRUM-161): tick the last 5 seconds and "bong" at zero, but only while I have not
        // answered. A placed card is auto-locked 300 ms before zero (SCRUM-171) and plays the chip
        // sound instead, so the player hears "your answer was taken", not "you were too late".
        if (_secondsLeft is > 0 and <= 5 && !Answered)
            _ = Sounds.PlayAsync(Sfx.Tick);
        if (_secondsLeft == 0)
        {
            if (!Answered)
                _ = Sounds.PlayAsync(Sfx.TimeUp);
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
        StopPreRoll();
        _goTimer?.Dispose();
        _goTimer = null;
        _go = false;
    }

    // ---- SCRUM-169: 3 - 2 - 1 - "Nu!" before the song ----

    // `wait` until the song: show the whole seconds left (e.g. 2.4 s shows 3) and tick down on the
    // boundaries, so the last number goes away exactly when the song starts. A tick sound per number.
    private void StartPreRoll(TimeSpan wait)
    {
        StopPreRoll();
        _startsIn = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        _ = Sounds.PlayAsync(Sfx.Tick);
        var first = wait - TimeSpan.FromSeconds(_startsIn.Value - 1);
        _preRollTimer = Time.CreateTimer(_ => InvokeAsync(PreRollTick), null, first, TimeSpan.FromSeconds(1));
        StateHasChanged();
    }

    // The song's own start (StartRoundAsync) takes "1" away, so this never goes below 1.
    private void PreRollTick()
    {
        if (_startsIn is not > 1)
            return;
        _startsIn--;
        _ = Sounds.PlayAsync(Sfx.Tick);
        StateHasChanged();
    }

    private void StopPreRoll()
    {
        _preRollTimer?.Dispose();
        _preRollTimer = null;
        _startsIn = null;
    }

    private void ShowGo()
    {
        StopPreRoll();
        _go = true;
        _ = Sounds.PlayAsync(Sfx.GameStart);
        _goTimer?.Dispose();
        _goTimer = Time.CreateTimer(_ => InvokeAsync(() =>
        {
            _go = false;
            StateHasChanged();
        }), null, GoShownFor, Timeout.InfiniteTimeSpan);
    }

    // I have answered this round: sent from this page, or the server has my stamp (e.g. after a reload).
    private bool Answered =>
        _answeredThisRound || (_me is { } me && _players.TryGetValue(me, out var self) && self.Stamp is not null);

    private async Task ConfirmAsync(int slotIndex)
    {
        // Marked and heard before the network round trip: the auto-lock sends 300 ms before zero, and
        // the "time up" sound at zero must already know the answer is on its way.
        _answeredThisRound = true;
        _ = Sounds.PlayAsync(Sfx.LockIn); // a chip on the table: the answer is locked
        try
        {
            // My own stamp arrives with PlayerAnswered like everyone else's.
            await Hub.SubmitAnswerAsync(GameId, _round, slotIndex);
        }
        catch (GameHubException e)
        {
            _answeredThisRound = false;
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
        _roundEnded = ended;
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
        StateHasChanged();
    });

    private Task OnGameEnded(GameEndedDto ended) => InvokeAsync(async () =>
    {
        _roundCts?.Cancel();
        StopCountdown();
        _ended = ended;
        await Audio.StopAsync();
        // With awards the result is revealed step by step (SCRUM-155): the sound comes at its end.
        if (ended.Awards is null)
            await PlayEndSoundAsync();
        foreach (var round in _sources.Keys.ToList())
            await ReleaseAsync(round);
        StateHasChanged();
    });

    private async Task PlayEndSoundAsync()
    {
        var me = _ended?.Standings.FirstOrDefault(s => s.UserId == _me);
        await Sounds.PlayAsync(me?.Placement == 1 ? Sfx.Winner : Sfx.GameOver);
    }

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

    // The effects slider: stored by the sound player itself, with a tick so the level can be heard.
    private async Task OnEffectsVolumeAsync(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var percent))
            return;
        _effectsVolume = Math.Clamp(percent / 100, 0, 1);
        await Sounds.SetVolumeAsync(_effectsVolume);
        await Sounds.PlayAsync(Sfx.Tick);
    }

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
