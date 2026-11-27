using System.Security.Claims;

using System.Net;

using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Domain.DTOs.Games;
using Frontend.Domain.DTOs.Lobbies;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Components.Pages;
using Frontend.Web.Games;
using Frontend.Web.Sound;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Microsoft.Extensions.Time.Testing;

namespace Frontend.Web.Tests;

// SCRUM-112: /spil/{gameId} with a fake hub, fake audio and fake time, so nothing waits for real seconds.
public class GamePageTests : BunitContext
{
    private static readonly Guid GameId = Guid.Parse("6f0c1d2e-3a4b-4c5d-8e9f-0a1b2c3d4e5f");
    private const int Me = 1, Bob = 2;

    private readonly FakeGameHub _hub = new();
    private readonly FakeGameAudio _audio = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSoundEffects _sounds = new();
    private readonly ILobbyApiClient _lobbies = Substitute.For<ILobbyApiClient>();
    private static readonly Guid LobbyId = Guid.Parse("8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11");

    public GamePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // localStorage, hand.js
        Services.AddSingleton<IGameHubClient>(_hub);
        Services.AddSingleton<IGameAudio>(_audio);
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton<ISoundEffects>(_sounds);
        Services.AddSingleton(_lobbies);
        AddAuthorization().SetAuthorized("alice").SetClaims(new Claim("sub", Me.ToString()));
        _hub.State = State();
    }

    private static CardDto Card(int year) => new($"Sang {year}", [$"Kunstner {year}"], year);
    private static string Url(int round) => $"https://media.example/songs/{round}.mp3";

    private static GameStateDto State(int round = 0, RoundStartedDto? current = null, params int[] prefetchRounds) => new(
        GameId,
        new GameSettingsDto(3, "80'er hits", 10, 30),
        round,
        10,
        [new PlayerSummaryDto(Me, "alice", 0, [Card(1976)]), new PlayerSummaryDto(Bob, "bob", 0, [Card(1976)])],
        [Card(1976)],
        (prefetchRounds.Length == 0 ? [1, 2] : prefetchRounds).Select(r => new PrefetchDto(r, Url(r))).ToList(),
        current);

    private static RoundStartedDto Round(int round, int startsInMs = 3000, int offset = 40, int answer = 30) =>
        new(round, 10, startsInMs, offset, answer);

    private IRenderedComponent<GamePage> RenderPage() => Render<GamePage>(p => p.Add(x => x.GameId, GameId));

    private static string Text(IRenderedComponent<GamePage> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    [Fact]
    public void BeforeTheFirstRound_ShowsMyCardsAndPlayers_AndWaits()
    {
        var cut = RenderPage();

        Assert.Single(cut.FindAll("[data-testid=card]"));
        Assert.Contains("1976", cut.Find("[data-testid=card]").TextContent);
        var rival = Assert.Single(cut.FindAll("[data-testid=rival]")); // only Bob: I am not at my own table
        Assert.Contains("bob", rival.TextContent);
        Assert.Contains("Venter på spillerne", cut.Find(".sl-banner").TextContent);
        Assert.Equal("Spillet starter, når alle er forbundet.", Text(cut, "message"));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
    }

    [Fact]
    public void Join_PrefetchesTheFiles_InOrder_AndSendsReadyForEach()
    {
        RenderPage();

        Assert.Equal([Url(1), Url(2)], _audio.Prefetched);
        Assert.Equal([1, 2], _hub.Ready);
    }

    [Fact]
    public async Task Prefetch_RetriesOnce_ThenStreamsTheUrl_AndStillSendsReady()
    {
        _hub.State = State(prefetchRounds: [1]);
        _audio.FailuresLeft[Url(1)] = 2; // fails twice
        var cut = RenderPage();

        Assert.Equal([Url(1), Url(1)], _audio.Prefetched);
        Assert.Equal([1], _hub.Ready);

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.Equal((Url(1), 40.0), _audio.Played.Single()));
    }

    [Fact]
    public async Task PrefetchMessage_DownloadsNewRounds_WithoutRepeatingOldOnes()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaisePrefetch(new PrefetchDto(2, Url(2)), new PrefetchDto(6, Url(6))));

        cut.WaitForAssertion(() => Assert.Equal([1, 2, 6], _hub.Ready));
        Assert.Equal([Url(1), Url(2), Url(6)], _audio.Prefetched);
    }

    [Fact]
    public async Task RoundStarted_PlaysAfterStartsInMs_FromTheOffset_AndCountsDown()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000, offset: 40, answer: 30)));
        Assert.Contains("Gør dig klar", cut.Find(".sl-banner").TextContent);
        _time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.Empty(_audio.Played);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(1), 40.0), _audio.Played.Single()));
        cut.WaitForAssertion(() => Assert.Equal("30", Text(cut, "time")));
        Assert.Contains("Nu spiller", cut.Find(".sl-banner").TextContent);
        _time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("29", Text(cut, "time")));
    }

    // ---- SCRUM-169: "next round" screen and 3-2-1 before the song ----

    [Fact]
    public async Task Countdown_Shows3_2_1_EndsWhenTheSongStarts_ThenNu()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000)));
        Assert.Contains("Runde 1 af 10", Text(cut, "round-countdown"));
        Assert.Equal("3", Text(cut, "countdown-number"));
        _time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("2", Text(cut, "countdown-number")));
        _time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("1", Text(cut, "countdown-number")));
        Assert.Empty(_audio.Played);

        _time.Advance(TimeSpan.FromSeconds(1)); // zero: the song starts at the same instant
        cut.WaitForAssertion(() => Assert.Single(_audio.Played));
        cut.WaitForAssertion(() => Assert.Equal("Nu!", Text(cut, "countdown-number")));
        Assert.Equal([Sfx.Tick, Sfx.Tick, Sfx.Tick], _sounds.Played); // "Nu!" is silent: the song is the signal

        _time.Advance(GamePage.GoShownFor);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=round-countdown]")));
    }

    [Fact]
    public async Task Countdown_LocksTheHand_UntilTheSongStarts()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000)));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.Equal("Sangen starter om lidt.", Text(cut, "message"));

        _time.Advance(TimeSpan.FromSeconds(3));
        cut.WaitForAssertion(() => Assert.Single(_audio.Played));
        cut.WaitForAssertion(() => Assert.DoesNotContain(cut.FindAll("[data-testid=message]"),
            m => m.TextContent.Contains("Sangen starter om lidt.")));
    }

    [Fact]
    public async Task Countdown_FollowsTheServersStart_NotTheMessagesArrival()
    {
        _hub.ServerClock = () => ServerNow;
        var cut = RenderPage();

        // The message was 1.4 s on its way: 1.6 s are left, shown as 2.
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000) with { PlaybackStartsAt = ServerNow.AddSeconds(1.6) }));
        Assert.Equal("2", Text(cut, "countdown-number"));
        _time.Advance(TimeSpan.FromMilliseconds(600)); // on the whole second before the song
        cut.WaitForAssertion(() => Assert.Equal("1", Text(cut, "countdown-number")));
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(_audio.Played);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        cut.WaitForAssertion(() => Assert.Equal("Nu!", Text(cut, "countdown-number")));
    }

    [Fact]
    public void RejoiningDuringTheCountdown_ShowsWhatIsLeft()
    {
        _hub.State = State(round: 1, current: Round(1, startsInMs: 1200), prefetchRounds: [1, 2]);
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Equal("2", Text(cut, "countdown-number")));
        _time.Advance(TimeSpan.FromMilliseconds(200));
        cut.WaitForAssertion(() => Assert.Equal("1", Text(cut, "countdown-number")));
    }

    [Fact]
    public async Task NoCountdown_WhenTheSongHasAlreadyStarted()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));

        cut.WaitForAssertion(() => Assert.Single(_audio.Played));
        Assert.Empty(cut.FindAll("[data-testid=round-countdown]"));
        Assert.DoesNotContain(Sfx.GameStart, _sounds.Played);
    }

    [Fact]
    public async Task Countdown_FromRound2_ShowsWhereIStand()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 0, false, 0, [Card(1976)]),
            new RoundResultDto(Bob, "bob", 1, true, 1, [Card(1976), Card(1982)])
        ])));

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 3000)));

        Assert.Contains("Runde 2 af 10", Text(cut, "round-countdown"));
        Assert.Equal("Du har 0 point · nr. 2 af 2", Text(cut, "countdown-standing"));
    }

    [Fact]
    public async Task RoundEndedDuringTheCountdown_StopsIt()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000)));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
            [new RoundResultDto(Me, "alice", null, false, 0, [Card(1976)])])));
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(cut.FindAll("[data-testid=round-countdown]"));
        Assert.Empty(_audio.Played);
    }

    // ---- SCRUM-168: everyone starts at the server's PlaybackStartsAt ----

    private static readonly TimeSpan ServerAhead = TimeSpan.FromMinutes(7); // a wrong device clock must not matter

    private DateTimeOffset ServerNow => _time.GetUtcNow() + ServerAhead;

    [Fact]
    public async Task WithAServerClock_PlaysAtPlaybackStartsAt_NotStartsInMsAfterArrival()
    {
        _hub.ServerClock = () => ServerNow;
        var cut = RenderPage();
        Assert.Equal(ClockSync.Samples, _hub.ServerTimeCalls);

        // The message was 1 s on its way: startsInMs still says 3000, but playback is 2 s from now.
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000) with { PlaybackStartsAt = ServerNow.AddSeconds(2) }));
        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Empty(_audio.Played);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(1), 40.0), _audio.Played.Single()));
        cut.WaitForAssertion(() => Assert.Equal("30", Text(cut, "time")));
    }

    [Fact]
    public async Task ArrivingLate_PlaysAtOnce_FurtherIntoTheSong_WithLessTimeLeft()
    {
        _hub.ServerClock = () => ServerNow;
        var cut = RenderPage();

        // Playback started 1.5 s ago on the server.
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, offset: 40, answer: 30) with { PlaybackStartsAt = ServerNow.AddSeconds(-1.5) }));

        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(1), 41.5), _audio.Played.Single()));
        cut.WaitForAssertion(() => Assert.Equal("29", Text(cut, "time"))); // 28.5 s left
        _time.Advance(TimeSpan.FromMilliseconds(500));                       // on the server's whole second
        cut.WaitForAssertion(() => Assert.Equal("28", Text(cut, "time")));
        _time.Advance(TimeSpan.FromSeconds(28));
        cut.WaitForAssertion(() => Assert.Equal("Tiden er gået.", Text(cut, "message"))); // exactly at the server's end
    }

    [Fact]
    public void ARejoinBetweenWholeSeconds_StartsTheSongAtTheExactMillisecond()
    {
        _hub.ServerClock = () => ServerNow;
        // The server moved the int fields by 5 whole seconds and playbackStartsAt with them; 400 ms more have gone.
        _hub.State = State(round: 1, current: Round(1, startsInMs: 0, offset: 45, answer: 25) with { PlaybackStartsAt = ServerNow.AddMilliseconds(-400) },
            prefetchRounds: [1, 2]);

        RenderPage();

        Assert.Equal(("blob:" + Url(1), 45.4), _audio.Played.Single());
    }

    [Fact]
    public async Task WithoutServerTime_AnOlderServer_FallsBackToStartsInMs()
    {
        _hub.ServerClock = null;
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 3000) with { PlaybackStartsAt = DateTimeOffset.UnixEpoch }));
        _time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.Empty(_audio.Played);
        _time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(1), 40.0), _audio.Played.Single()));
    }

    [Fact]
    public async Task WhenTheTimeIsUp_TheHandLocks_AndTheSongPlaysOn()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.Equal("15", Text(cut, "time")));

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal("Tiden er gået.", Text(cut, "message")));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.True(_audio.Playing); // SCRUM-159: it stops at the next round or the game end
    }

    // SCRUM-159: everyone answered, so the round ends early; the song plays on through the reveal.
    [Fact]
    public async Task TheSong_PlaysOnThroughTheReveal_AndStopsWhenTheNextRoundStarts()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.True(_audio.Playing));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 1, true, 1, [Card(1976), Card(1982)]),
            new RoundResultDto(Bob, "bob", 0, false, 0, [Card(1976)])
        ])));

        Assert.True(_audio.Playing);
        Assert.DoesNotContain("blob:" + Url(1), _audio.Revoked); // still playing from it

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 3000)));

        cut.WaitForAssertion(() => Assert.False(_audio.Playing)); // silent during "Gør dig klar"
        Assert.Contains("blob:" + Url(1), _audio.Revoked);
        Assert.DoesNotContain("blob:" + Url(2), _audio.Revoked); // the next song is kept
        _time.Advance(TimeSpan.FromSeconds(3));
        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(2), 40.0), _audio.Played[^1]));
        Assert.True(_audio.Playing);
    }

    // Found in the end-to-end test: everyone answered with 8 s left, and "Tid" stayed on 8 through the reveal.
    [Fact]
    public async Task ARoundThatEndsEarly_SetsTheTimerToZero_AndTheNextRoundStartsItAgain()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        for (var i = 0; i < 7; i++)
            _time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("8", Text(cut, "time")));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 0, false, 0, [Card(1976)]),
            new RoundResultDto(Bob, "bob", 0, false, 0, [Card(1976)])
        ])));

        Assert.Equal("0", Text(cut, "time"));
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("0", Text(cut, "time")); // stays 0 through the reveal

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.Equal("15", Text(cut, "time")));
    }

    [Fact]
    public async Task TheLastSong_PlaysOnThroughTheReveal_AndStopsAtTheGameEnd()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 0, false, 0, [Card(1976)]),
            new RoundResultDto(Bob, "bob", 0, false, 0, [Card(1976)])
        ])));
        Assert.True(_audio.Playing);

        await cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId,
            [new StandingDto(Me, "alice", 0, 0, 1, 1, []), new StandingDto(Bob, "bob", 0, 0, 1, 1, [])])));

        Assert.False(_audio.Playing);
        Assert.Contains("blob:" + Url(1), _audio.Revoked);
    }

    [Fact]
    public async Task BlockedAutoplay_ShowsAPlayButton_ThatJoinsWhereTheOthersAre()
    {
        _audio.BlockAutoplay = true;
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, offset: 40)));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=play]")));

        _time.Advance(TimeSpan.FromSeconds(4)); // the player clicks 4 s late
        _audio.BlockAutoplay = false;
        cut.Find("[data-testid=play]").Click();

        Assert.Equal(44.0, _audio.Played[^1].Offset, precision: 3);
        Assert.Empty(cut.FindAll("[data-testid=play]"));
    }

    [Fact]
    public async Task Confirm_SendsTheChosenSlot_ForTheCurrentRound()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(3, startsInMs: 0)));

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[1].Click(); // after 1976
        cut.Find("[data-testid=confirm]").Click();

        Assert.Equal([(3, 1)], _hub.Answers);
    }

    // SCRUM-171: a card placed without "Bekræft" is sent just before the time runs out.
    [Fact]
    public async Task APlacedCard_IsLockedAutomatically_JustBeforeTheTimeRunsOut()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[0].Click(); // before 1976

        _time.Advance(TimeSpan.FromSeconds(15) - GamePage.AutoLockLead - TimeSpan.FromMilliseconds(1));
        Assert.Empty(_hub.Answers);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        cut.WaitForAssertion(() => Assert.Equal([(2, 0)], _hub.Answers));
        Assert.Equal("Svar afgivet – låst", Text(cut, "message"));

        _time.Advance(GamePage.AutoLockLead); // the time is up: still only the one answer
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        Assert.Single(_hub.Answers);
    }

    [Fact]
    public async Task WithNothingPlaced_NoAnswerIsSent_WhenTheTimeRunsOut()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.Equal("15", Text(cut, "time")));

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal("Tiden er gået.", Text(cut, "message")));
        Assert.Empty(_hub.Answers);
    }

    [Fact]
    public async Task AConfirmedAnswer_IsNotSentAgain_WhenTheTimeRunsOut()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        Assert.Equal([(1, 1)], _hub.Answers);
    }

    [Fact]
    public async Task PlayerAnswered_StampsTheRivalsSeat_WithOrderAndTime_AndANewRoundClearsIt()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));

        await cut.InvokeAsync(() => _hub.RaisePlayerAnswered(Me, 2100)); // I answered first
        await cut.InvokeAsync(() => _hub.RaisePlayerAnswered(Bob, 4200));
        Assert.Equal("2. · 4,2 s", cut.Find("[data-testid=rival] [data-testid=rival-stamp] .rt-st").TextContent);

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=rival-stamp]")));
    }

    [Fact]
    public async Task RoundEnded_UpdatesTheRivalsHandAndStreak()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", null, false, 0, [Card(1976)]),
            new RoundResultDto(Bob, "bob", 1, true, 1, [Card(1976), Card(1982)], 3000, [1976, null])
        ])));

        var seat = cut.Find("[data-testid=rival]");
        Assert.Equal("2 kort", seat.QuerySelector("[data-testid=rival-count]")!.TextContent);
        Assert.Contains("new", seat.QuerySelectorAll("[data-testid=rival-card]")[1].ClassList);
        Assert.Single(seat.QuerySelectorAll("[data-testid=rival-track] b.y"));
    }

    [Fact]
    public void AfterAReload_TheTableIsRebuiltFromTheServersState()
    {
        _hub.State = State(round: 3) with
        {
            Players =
            [
                new PlayerSummaryDto(Me, "alice", 1, [Card(1976), Card(1990)]),
                new PlayerSummaryDto(Bob, "bob", 2, [Card(1960), Card(1976), Card(1999)],
                    [new RoundHistoryDto(2, true, 5000), new RoundHistoryDto(1, true, 3000)], AnsweredOrder: 1)
            ]
        };

        var cut = RenderPage();

        var seat = cut.Find("[data-testid=rival]");
        Assert.Equal(["60", "76", "99"], seat.QuerySelectorAll("[data-testid=rival-card]").Select(c => c.TextContent));
        Assert.Equal("2 i træk", seat.QuerySelector("[data-testid=rival-run]")!.TextContent);
        Assert.Equal("1.", seat.QuerySelector("[data-testid=rival-stamp] .rt-st")!.TextContent);
    }

    [Fact]
    public async Task RoundEnded_RevealsMyResult_AndTheNextRoundFreesTheSong_AndShowsMyNewHand()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 1, true, 1, [Card(1976), Card(1982)]),
            new RoundResultDto(Bob, "bob", null, false, 0, [Card(1976)])
        ])));

        Assert.Equal("Rigtigt! Kortet bliver i din hånd.", Text(cut, "message"));
        Assert.Single(cut.FindAll("[data-testid=card]")); // the reveal still shows the placed card, not a duplicate

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.Contains("blob:" + Url(1), _audio.Revoked));

        cut.WaitForAssertion(() => Assert.Equal(["1976", "1982"],
            cut.FindAll("[data-testid=card] .gc-year").Select(e => e.TextContent)));
        Assert.Equal(3, cut.FindAll("[data-testid=slot]").Count);
    }

    [Fact]
    public async Task GameEnded_ShowsTheStandings_WithMeHighlighted()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId,
        [
            new StandingDto(Bob, "bob", 6, 6, 9, 1, []),
            new StandingDto(Me, "alice", 5, 5, 10, 2, [])
        ])));

        var rows = cut.FindAll("[data-testid=standing]");
        Assert.Contains("bob", rows[0].TextContent);
        Assert.Contains("6 point", rows[0].TextContent);
        Assert.Contains("me", rows[1].ClassList);
        Assert.Contains("(dig)", rows[1].TextContent);
    }

    [Fact]
    public async Task DeckAndDiscard_FollowTheRounds()
    {
        var cut = RenderPage();
        Assert.Equal("10", Text(cut, "deck-count"));
        Assert.Equal("0", Text(cut, "discard-count"));

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.Equal("9", Text(cut, "deck-count")));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 0, false, 0, [Card(1976)]),
            new RoundResultDto(Bob, "bob", null, false, 0, [Card(1976)])
        ])));

        Assert.Equal("1", Text(cut, "discard-count"));
        cut.Find("[data-testid=discard]").Click();
        var song = Assert.Single(cut.FindAll("[data-testid=song]"));
        Assert.Contains("Sang 1982", song.TextContent);
        Assert.Contains("forkert", song.TextContent);
    }

    [Fact]
    public void AfterAReload_TheDiscardPileIsRebuiltFromPastSongsAndMyHistory()
    {
        _hub.State = State(round: 3) with
        {
            Players =
            [
                new PlayerSummaryDto(Me, "alice", 1, [Card(1976), Card(1985)],
                    [new RoundHistoryDto(1, true, 3000), new RoundHistoryDto(2, false, 4000), new RoundHistoryDto(3, false, null)]),
                new PlayerSummaryDto(Bob, "bob", 0, [Card(1976)])
            ],
            PastSongs = [new PastSongDto(1, Card(1985)), new PastSongDto(2, Card(2003)), new PastSongDto(3, Card(1995))]
        };

        var cut = RenderPage();
        cut.Find("[data-testid=discard]").Click();

        var rows = cut.FindAll("[data-testid=song]");
        Assert.Equal(2, rows.Count);
        Assert.Contains("forkert", rows[0].TextContent);    // round 2: answered, wrong
        Assert.Contains("intet svar", rows[1].TextContent); // round 3: no answer
        Assert.Equal("7", Text(cut, "deck-count"));
    }

    [Fact]
    public async Task GameEnd_CanShowEveryRoundsSong()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
            [new RoundResultDto(Me, "alice", 1, true, 1, [Card(1976), Card(1982)])])));
        await cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId, [new StandingDto(Me, "alice", 1, 1, 1, 1, [])])));

        cut.Find("[data-testid=all-songs]").Click();

        var song = Assert.Single(cut.FindAll("[data-testid=song]"));
        Assert.Contains("i din hånd", song.TextContent);
        Assert.Equal("Alle spillets sange", cut.Find("#songs-title").TextContent);
    }

    [Fact]
    public void JoinError_ShowsTheDanishMessage()
    {
        _hub.JoinError = new GameHubException("Du er ikke med i det her spil.");

        var cut = RenderPage();

        Assert.Contains("Du er ikke med i det her spil.", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=hand-of-cards]"));
    }

    [Fact]
    public async Task Reconnect_ShowsAWarning_ThenResumesTheRunningRoundFromTheServersState()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseConnectionLost());
        Assert.NotNull(cut.Find("[data-testid=connection-lost]"));

        await cut.InvokeAsync(() => _hub.RaiseRejoined(State(round: 2, current: Round(2, startsInMs: 0, offset: 55, answer: 21), prefetchRounds: [2, 3])));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=connection-lost]")));
        cut.WaitForAssertion(() => Assert.Equal(("blob:" + Url(2), 55.0), _audio.Played[^1]));
        Assert.Equal("21", Text(cut, "time"));
    }

    [Fact]
    public void Volume_IsAppliedAndRemembered()
    {
        var cut = RenderPage();

        cut.Find("[data-testid=volume]").Input("40");

        Assert.Equal(0.4, _audio.Volumes[^1], precision: 3);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem"
                                                    && (string?)i.Arguments[0] == GamePage.VolumeKey
                                                    && (string?)i.Arguments[1] == "0.4");
    }

    [Fact]
    public void EffectsVolume_IsSeparateFromTheMusic_AndPlaysATickToHearIt()
    {
        var cut = RenderPage();

        cut.Find("[data-testid=effects-volume]").Change("90");

        Assert.Equal(0.9, _sounds.Volume!.Value, precision: 3);
        Assert.Equal(Sfx.Tick, _sounds.Played[^1]);
        Assert.DoesNotContain(0.9, _audio.Volumes); // the song keeps its own volume
    }

    // SCRUM-164/165: sounds in the round.

    [Fact]
    public async Task Round_TicksTheLastFiveSeconds_AndTimeUp()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        Assert.Empty(_sounds.Played); // a new round makes no sound of its own

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal(
            [Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.TimeUp], _sounds.Played));
    }

    [Fact]
    public async Task Confirm_PlaysTheLockInSound_AndACorrectAnswerPlaysCorrect()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();
        Assert.Equal(Sfx.LockIn, _sounds.Played[^1]);

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
            [new RoundResultDto(Me, "alice", 1, true, 1, [Card(1976), Card(1982)])])));

        Assert.Equal(Sfx.Correct, _sounds.Played[^1]);
    }

    [Fact]
    public async Task NoAnswer_PlaysWrong()
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
            [new RoundResultDto(Me, "alice", null, false, 0, [Card(1976)])])));

        Assert.Equal([Sfx.Wrong], _sounds.Played);
    }

    [Fact]
    public async Task AutoLock_PlaysTheChip_AndNoTicksOrTimeUpAfterwards()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[0].Click(); // placed, but no "Bekræft"

        for (var i = 0; i < 14; i++)
            _time.Advance(TimeSpan.FromSeconds(1)); // 1 s left: ticks at 5, 4, 3, 2 and 1
        _time.Advance(TimeSpan.FromSeconds(1) - GamePage.AutoLockLead); // auto-lock
        cut.WaitForAssertion(() => Assert.Single(_hub.Answers));
        _time.Advance(GamePage.AutoLockLead); // zero

        cut.WaitForAssertion(() => Assert.Equal(
            [Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.Tick, Sfx.LockIn], _sounds.Played));
    }

    [Fact]
    public async Task AfterBekraeft_TheCountdownIsSilent()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled")));
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal([Sfx.LockIn], _sounds.Played));
    }

    [Theory]
    [InlineData(1, Sfx.Winner)]
    [InlineData(2, Sfx.GameOver)]
    public async Task GameEnded_PlaysWinnerOnlyForFirstPlace(int myPlacement, Sfx expected)
    {
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId,
            [new StandingDto(Me, "alice", 5, 5, 10, myPlacement, [])])));

        Assert.Equal([expected], _sounds.Played);
    }

    // SCRUM-155: with awards, the end is revealed step by step; buttons and the end sound come after it.
    [Fact]
    public async Task GameEndedWithAwards_RevealsStepByStep_ThenShowsTheButtonsAndPlaysTheSound()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"spil/{GameId}?lobby={LobbyId}");
        var cut = RenderPage();

        await cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId,
            [new StandingDto(Me, "alice", 4, 3, 3, 1, []), new StandingDto(Bob, "bob", 2, 2, 3, 2, [])],
            [new AwardDto("streak", [Me], [new AwardContenderDto(Me, 3, true), new AwardContenderDto(Bob, 1, false)])])));

        Assert.Equal("Bonus!", Text(cut, "er-banner"));
        Assert.Empty(cut.FindAll("[data-testid=play-again]"));
        Assert.Equal([Sfx.BonusIntro], _sounds.Played); // "Bonus!" is heard, the end sound waits

        cut.Find("[data-testid=skip]").Click();

        Assert.Equal("Spil igen", Text(cut, "play-again"));
        Assert.Equal([Sfx.BonusIntro, Sfx.Winner], _sounds.Played);
        Assert.Contains("4 point (+1 bonus)", cut.Find("[data-user='1']").TextContent);
    }

    // SCRUM-154: the guess map and lanes come with RoundEnded and go when the next round starts.
    [Fact]
    public async Task RoundEnded_ShowsTheRecap_UntilTheNextRoundStarts()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0)));
        Assert.Empty(cut.FindAll("[data-testid=round-recap]"));

        await cut.InvokeAsync(() => _hub.RaiseRoundEnded(new RoundEndedDto(1, Card(1982),
        [
            new RoundResultDto(Me, "alice", 1, true, 1, [Card(1976), Card(1982)], 5000, [1976, null]),
            new RoundResultDto(Bob, "bob", null, false, 0, [Card(1976)])
        ])));

        Assert.Single(cut.FindAll("[data-testid=round-recap]"));
        Assert.Equal(2, cut.FindAll("[data-testid=guess]").Count);

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0)));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=round-recap]")));
    }

    // SCRUM-172: "Spil igen" and "Forlad" on the end screen.
    private IRenderedComponent<GamePage> RenderEndScreen(bool withLobby = true)
    {
        if (withLobby)
            Services.GetRequiredService<NavigationManager>().NavigateTo($"spil/{GameId}?lobby={LobbyId}");
        var cut = RenderPage();
        cut.InvokeAsync(() => _hub.RaiseGameEnded(new GameEndedDto(GameId,
            [new StandingDto(Me, "alice", 3, 3, 3, 1, []), new StandingDto(Bob, "bob", 1, 1, 3, 2, [])])));
        return cut;
    }

    private static LobbyDto ReopenedLobby() => new(LobbyId, "ABC234", Me, true,
        new LobbySettingsDto(3, "80'er hits", 30, 10, 4), [new LobbyPlayerDto(Me, "alice", false)], "Waiting");

    private static ApiException Conflict(string detail) =>
        new(HttpStatusCode.Conflict, detail, new ProblemDetailsDto { Status = 409, Detail = detail });

    [Fact]
    public void TheEndScreen_OffersPlayAgainAndLeave()
    {
        var cut = RenderEndScreen();

        Assert.Equal("Spil igen", Text(cut, "play-again"));
        Assert.Equal("Forlad", Text(cut, "leave"));
    }

    [Fact]
    public void WithoutALobby_TheEndScreenOnlyLeadsToTheFrontPage()
    {
        var cut = RenderEndScreen(withLobby: false);

        Assert.Empty(cut.FindAll("[data-testid=play-again]"));
        Assert.Contains("Til forsiden", cut.Find(".game-end").TextContent);
    }

    [Fact]
    public void PlayAgain_GoesBackToTheSameLobby()
    {
        _lobbies.PlayAgainAsync(LobbyId, Arg.Any<CancellationToken>()).Returns(ReopenedLobby());
        var cut = RenderEndScreen();

        cut.Find("[data-testid=play-again]").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/lobby/{LobbyId}", navigation.Uri));
    }

    // GameEnded reaches the players before LobbyService has handled GameFinished: 409, try again.
    [Fact]
    public async Task PlayAgain_TriesAgain_WhileTheLobbyHasNotReopenedYet()
    {
        _lobbies.PlayAgainAsync(LobbyId, Arg.Any<CancellationToken>())
            .Returns(_ => throw Conflict("The game has not finished yet."), _ => Task.FromResult(ReopenedLobby()));
        var cut = RenderEndScreen();
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.Find("[data-testid=play-again]").Click();
        Assert.Equal("Vender tilbage…", Text(cut, "play-again"));
        Assert.DoesNotContain("lobby/", navigation.Uri);

        await cut.InvokeAsync(() => _time.Advance(GamePage.PlayAgainRetryDelay));

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/lobby/{LobbyId}", navigation.Uri));
        await _lobbies.Received(2).PlayAgainAsync(LobbyId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PlayAgain_ShowsWhyItFailed_WhenTheLobbyIsFull()
    {
        _lobbies.PlayAgainAsync(LobbyId, Arg.Any<CancellationToken>()).ThrowsAsync(Conflict("The lobby is full."));
        var cut = RenderEndScreen();

        cut.Find("[data-testid=play-again]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Lobbyen er fuld.", cut.Find(".game-end").TextContent));
        Assert.Equal("Spil igen", Text(cut, "play-again"));
        _lobbies.Received(1).PlayAgainAsync(LobbyId, Arg.Any<CancellationToken>());
    }
}
