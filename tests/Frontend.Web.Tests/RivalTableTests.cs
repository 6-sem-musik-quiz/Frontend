using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;

namespace Frontend.Web.Tests;

// SCRUM-152: the card table. Rivals show cards face up, a lock stamp (when, never where) and a streak.
public class RivalTableTests : BunitContext
{
    private static CardDto Card(int year) => new($"Sang {year}", [$"Kunstner {year}"], year);
    private static readonly CardDto Song = new("Billie Jean", ["Michael Jackson"], 1982);

    private static RivalView Rival(int id = 2, string name = "Asger", int[]? years = null, bool[]? history = null,
        RivalStamp? stamp = null, RivalReveal? reveal = null) =>
        new(id, name, (years ?? [1972, 1985, 1991]).Select(Card).ToList(), history ?? [], stamp, reveal);

    private IRenderedComponent<RivalTable> RenderTable(params RivalView[] rivals) =>
        Render<RivalTable>(p => p.Add(x => x.Rivals, rivals).Add(x => x.TotalRounds, 10));

    [Fact]
    public void Seats_ShowNameCountAndFaceUpCards_WithTitleAsTooltip()
    {
        var cut = RenderTable(Rival(), Rival(3, "Benjamin", [1979]));

        var seats = cut.FindAll("[data-testid=rival]");
        Assert.Equal(2, seats.Count);
        Assert.Contains("Asger", seats[0].TextContent);
        Assert.Equal("3 kort", seats[0].QuerySelector("[data-testid=rival-count]")!.TextContent);
        var cards = seats[0].QuerySelectorAll("[data-testid=rival-card]");
        Assert.Equal(["72", "85", "91"], cards.Select(c => c.TextContent));
        Assert.Equal("1985: Sang 1985 – Kunstner 1985", cards[1].GetAttribute("title"));
    }

    [Fact]
    public void TheHand_TellsTheCssHowManyCardsToFit_SoItNeverScrolls()
    {
        var cut = RenderTable(Rival(years: Enumerable.Range(0, 20).Select(i => 1960 + i * 3).ToArray()));

        Assert.Equal("--n:20", cut.Find(".rt-hand").GetAttribute("style"));
    }

    [Fact]
    public void Stamp_ShowsOrderAndSecondsWithADanishComma_AndNothingAboutThePlacement()
    {
        var cut = RenderTable(Rival(stamp: new RivalStamp(1, 4200)), Rival(3, "Benjamin", stamp: new RivalStamp(2, null)));

        var stamps = cut.FindAll("[data-testid=rival-stamp]");
        Assert.Equal("1. · 4,2 s", stamps[0].QuerySelector(".rt-st")!.TextContent);
        Assert.Equal("2.", stamps[1].QuerySelector(".rt-st")!.TextContent); // after a reload: order only
        Assert.Equal(3, cut.FindAll("[data-testid=rival]")[0].QuerySelectorAll("[data-testid=rival-card]").Length); // hand unchanged
        Assert.Contains("har svaret som nummer 1", cut.FindAll("[data-testid=rival]")[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void StreakTrack_HasASquarePerRound_AndShowsTheCurrentRun()
    {
        var cut = RenderTable(Rival(history: [false, true, true, true]), Rival(3, "Benjamin", history: [true, false]));

        var tracks = cut.FindAll("[data-testid=rival-track]");
        Assert.Equal(10, tracks[0].QuerySelectorAll("b").Length);
        Assert.Equal(["n", "y", "y", "y"], tracks[0].QuerySelectorAll("b.y, b.n").Select(b => b.ClassName));
        Assert.Equal("3 i træk", tracks[0].QuerySelector("[data-testid=rival-run]")!.TextContent);
        Assert.Null(tracks[1].QuerySelector("[data-testid=rival-run]")); // a run of 0 is not shown
    }

    [Fact]
    public void RightAnswerAtRoundEnd_ShowsTheNewCardSlidingIn_AndHidesTheStamp()
    {
        // After RoundEnded the rival's cards already include the song (the server's new timeline).
        var cut = RenderTable(new RivalView(2, "Asger", [Card(1972), Song, Card(1985)], [true],
            new RivalStamp(1, 4200), new RivalReveal(Song, Correct: true, Slot: 1)));

        var cards = cut.FindAll("[data-testid=rival-card]");
        Assert.Equal(["72", "82", "85"], cards.Select(c => c.TextContent));
        Assert.Contains("new", cards[1].ClassList);
        Assert.Empty(cut.FindAll("[data-testid=rival-stamp]"));
    }

    [Fact]
    public void WrongAnswerAtRoundEnd_ShowsTheSongAtTheirSlot_FallingOut()
    {
        var cut = RenderTable(Rival(years: [1972, 1985, 1991], reveal: new RivalReveal(Song, Correct: false, Slot: 2)));

        var cards = cut.FindAll("[data-testid=rival-card]");
        Assert.Equal(["72", "85", "82", "91"], cards.Select(c => c.TextContent));
        Assert.Contains("out", cards[2].ClassList);
        Assert.Equal("3 kort", cut.Find("[data-testid=rival-count]").TextContent); // the count is unchanged
    }

    [Fact]
    public void NoAnswerAtRoundEnd_LeavesTheHandAsItWas()
    {
        var cut = RenderTable(Rival(reveal: new RivalReveal(Song, Correct: false, Slot: null)));

        Assert.DoesNotContain(cut.FindAll("[data-testid=rival-card]"), c => c.ClassList.Contains("out") || c.ClassList.Contains("new"));
        Assert.Equal(3, cut.FindAll("[data-testid=rival-card]").Count);
    }

    [Theory]
    [InlineData(new[] { true, true, false }, 0)]
    [InlineData(new[] { false, true, true }, 2)]
    [InlineData(new bool[0], 0)]
    public void CurrentRun_CountsRightAnswersAtTheEnd(bool[] history, int expected) =>
        Assert.Equal(expected, RivalTable.CurrentRun(history));
}
