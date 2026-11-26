using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Frontend.Web.Tests;

// SCRUM-154: the guess map and the answer-time lanes after a round. The song is from 1982.
public class RoundRecapTests : BunitContext
{
    private const int Me = 1, Bob = 2, Carl = 3, Dan = 4;
    private readonly FakeTimeProvider _time = new();

    public RoundRecapTests() => Services.AddSingleton<TimeProvider>(_time);

    private static readonly CardDto Song = new("Billie Jean", ["Michael Jackson"], 1982);

    private static RoundEndedDto Ended() => new(3, Song,
    [
        new RoundResultDto(Me, "alice", 1, true, 3, [], 6100, [1976, 1985]),   // right, between two cards
        new RoundResultDto(Bob, "bob", 2, false, 2, [], 4200, [1991, null]),   // wrong, after the last card
        new RoundResultDto(Carl, "carl", 0, true, 4, [], 9800, [null, 1990]),  // right, before the first card
        new RoundResultDto(Dan, "dan", null, false, 1, [], null, null)         // no answer
    ]);

    private IRenderedComponent<RoundRecap> RenderRecap() =>
        Render<RoundRecap>(p => p.Add(x => x.Ended, Ended()).Add(x => x.Me, Me).Add(x => x.AnswerTimeSeconds, 30));

    private static double LeftPct(AngleSharp.Dom.IElement e) => StyleValue(e, "left");
    private static double WidthPct(AngleSharp.Dom.IElement e) => StyleValue(e, "width");

    private static double StyleValue(AngleSharp.Dom.IElement e, string prop)
    {
        var style = e.GetAttribute("style")!;
        var part = style.Split(';').Select(s => s.Trim()).First(s => s.StartsWith(prop + ":"));
        return double.Parse(part[(prop.Length + 1)..].Trim().TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EachAnswer_IsARange_GreenIfRight_RustIfWrong_AndMineIsOutlined()
    {
        var cut = RenderRecap();

        var mine = cut.Find("[data-user='1'] .rr-range");
        Assert.Contains("y", mine.ClassList);
        Assert.Contains("me", mine.ClassList);
        Assert.Contains("Dig 1976–1985", mine.TextContent);

        var bob = cut.Find("[data-user='2'] .rr-range");
        Assert.Contains("n", bob.ClassList);
        Assert.DoesNotContain("me", bob.ClassList);
        Assert.Contains("bob efter 1991", bob.TextContent);

        // The range sits between its years on the axis.
        Assert.True(LeftPct(mine) > 0 && LeftPct(mine) + WidthPct(mine) < 100);
    }

    [Fact]
    public void OpenEnds_RunToTheEdgeOfTheAxis()
    {
        var cut = RenderRecap();

        var after = cut.Find("[data-user='2'] .rr-range");   // after 1991: to the right edge
        Assert.Equal(100, LeftPct(after) + WidthPct(after), 1);
        var before = cut.Find("[data-user='3'] .rr-range");  // before 1990: from the left edge
        Assert.Equal(0, LeftPct(before));
        Assert.Contains("carl før 1990", before.TextContent);
    }

    [Fact]
    public void TheRealYear_IsMarkedOnTheAxis_InsideMyRange()
    {
        var cut = RenderRecap();

        var line = cut.Find("[data-testid=true-year]");
        Assert.Equal("1982", line.TextContent.Trim());
        var mine = cut.Find("[data-user='1'] .rr-range");
        Assert.InRange(LeftPct(line), LeftPct(mine), LeftPct(mine) + WidthPct(mine));
    }

    [Fact]
    public void NoAnswer_SaysSo_AndHasNoRange()
    {
        var cut = RenderRecap();

        var dan = cut.Find("[data-testid=guess][data-user='4']");
        Assert.Contains("dan: intet svar", dan.TextContent);
        Assert.Empty(dan.QuerySelectorAll(".rr-range"));
    }

    [Fact]
    public void Lanes_AreSortedByTime_WithNoAnswerLast_AndMineHighlighted()
    {
        var cut = RenderRecap();

        var lanes = cut.FindAll("[data-testid=lane]");
        Assert.Equal(["2", "1", "3", "4"], lanes.Select(l => l.GetAttribute("data-user")));
        Assert.Equal(["4,2 s", "6,1 s", "9,8 s", "–"], cut.FindAll("[data-testid=lane-time]").Select(t => t.TextContent.Trim()));
        Assert.Contains("me", lanes[1].ClassList);
        Assert.Contains("Dig", lanes[1].TextContent);
        Assert.Contains("n", lanes[0].QuerySelector("i")!.ClassList); // bob: fast but wrong
        Assert.Empty(lanes[3].QuerySelectorAll("i"));                   // dan: no diamond
    }

    [Fact]
    public void Axis_CoversTheAnswersAndTheSong_WithRoom_AndTicksFitTheSpan()
    {
        var axis = GuessAxis.For(Ended()); // 1976-1991: a short axis, ticks every 5 years

        Assert.True(axis.Min < 1976 && axis.Max > 1991);
        Assert.Equal([1975, 1980, 1985, 1990], axis.Ticks);

        var wide = GuessAxis.For(Ended() with { Results = [new RoundResultDto(Me, "alice", 0, true, 0, [], 1000, [1950, 2020])] });
        Assert.All(wide.Ticks, t => Assert.Equal(0, t % 20)); // over 60 years: every 20
    }

    [Fact]
    public void NextRound_CountsDownThePause_ThenWaits()
    {
        var cut = RenderRecap();
        Assert.Equal("Næste runde om 8 s", cut.Find("[data-testid=next-round]").TextContent.Trim());

        _time.Advance(TimeSpan.FromSeconds(3));
        cut.WaitForAssertion(() => Assert.Equal("Næste runde om 5 s", cut.Find("[data-testid=next-round]").TextContent.Trim()));

        _time.Advance(TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Equal("Venter på de andre …", cut.Find("[data-testid=next-round]").TextContent.Trim()));
    }
}
