using System.Security.Claims;

using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Pages;
using Frontend.Web.Games;

using Microsoft.Extensions.DependencyInjection;
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

    public GamePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // localStorage, hand.js
        Services.AddSingleton<IGameHubClient>(_hub);
        Services.AddSingleton<IGameAudio>(_audio);
        Services.AddSingleton<TimeProvider>(_time);
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

    [Fact]
    public async Task WhenTheTimeIsUp_TheHandLocks_AndTheSongStops()
    {
        var cut = RenderPage();
        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(1, startsInMs: 0, answer: 15)));
        cut.WaitForAssertion(() => Assert.Equal("15", Text(cut, "time")));

        for (var i = 0; i < 15; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => Assert.Equal("Tiden er gået.", Text(cut, "message")));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.Equal(1, _audio.Stops);
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
    public async Task RoundEnded_RevealsMyResult_FreesTheSong_AndTheNextRoundShowsMyNewHand()
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
        Assert.Contains("blob:" + Url(1), _audio.Revoked);
        Assert.Single(cut.FindAll("[data-testid=card]")); // the reveal still shows the placed card, not a duplicate

        await cut.InvokeAsync(() => _hub.RaiseRoundStarted(Round(2, startsInMs: 0)));

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
}
