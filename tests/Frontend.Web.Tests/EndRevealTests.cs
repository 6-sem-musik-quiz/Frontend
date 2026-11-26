using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Frontend.Web.Tests;

// SCRUM-155: the staged end screen. Four players; the server's standings already include the bonus:
//   asger 6 + Stimen = 7 (1st), bob 6 (2nd), carl 5 + Lynet = 6 (2nd, shared), dan 4 (4th).
public class EndRevealTests : BunitContext
{
    private const int Asger = 1, Bob = 2, Carl = 3, Dan = 4;
    private readonly FakeTimeProvider _time = new();

    public EndRevealTests() => Services.AddSingleton<TimeProvider>(_time);

    private static GameEndedDto Ended(IReadOnlyList<AwardDto>? awards = null) => new(Guid.NewGuid(),
        [
            new StandingDto(Asger, "asger", 7, 6, 10, 1, []),
            new StandingDto(Bob, "bob", 6, 6, 10, 2, []),
            new StandingDto(Carl, "carl", 6, 5, 9, 2, []),
            new StandingDto(Dan, "dan", 4, 4, 8, 4, [])
        ],
        awards ??
        [
            new AwardDto("fastest", [Carl],
            [
                new AwardContenderDto(Carl, 4100, true), new AwardContenderDto(Asger, 6050, true),
                new AwardContenderDto(Bob, 7400, true), new AwardContenderDto(Dan, null, false)
            ]),
            new AwardDto("streak", [Asger],
            [
                new AwardContenderDto(Asger, 5, true), new AwardContenderDto(Bob, 4, true),
                new AwardContenderDto(Carl, 3, true), new AwardContenderDto(Dan, 1, false)
            ])
        ]);

    private static string Name(EndRevealScript s, int id) => s.Rows.Single(r => r.Standing.UserId == id).Standing.UserName;

    // Runs every beat and writes down what each one changed that is visible.
    private static List<string> Trace(EndRevealScript script)
    {
        var trace = new List<string>();
        while (script.NextWait is not null)
        {
            var shownBefore = script.Current?.Contenders.Where(c => c.Shown).Select(c => c.Name).ToHashSet() ?? [];
            var placedBefore = script.Rows.Where(r => r.Shown).ToHashSet();
            var (banner, award, stamped) = (script.Banner, script.Current?.Title, script.Current?.Stamped);
            script.Step();

            if (script.Banner != banner && script.Banner is not null) trace.Add($"banner {script.Banner}");
            if (script.Current?.Title != award && script.Current is not null) trace.Add($"award {script.Current.Title}");
            foreach (var c in script.Current?.Contenders.Where(c => c.Shown && !shownBefore.Contains(c.Name)) ?? [])
                trace.Add($"value {c.Name} {c.Value}");
            if (script.Current?.Stamped == true && stamped != true) trace.Add("stamp");
            foreach (var r in script.Rows.Where(r => r.Shown && !placedBefore.Contains(r)))
                trace.Add($"place {r.Standing.UserName} {r.Standing.Placement}");
        }
        return trace;
    }

    [Fact]
    public void Starts_WithBonus_AndEverySlotFaceDown()
    {
        var script = new EndRevealScript(Ended());

        Assert.Equal("Bonus!", script.Banner);
        Assert.All(script.Rows, r => Assert.False(r.Shown));
    }

    [Fact]
    public void Steps_ComeInTheDesignedOrder()
    {
        var trace = Trace(new EndRevealScript(Ended()));

        Assert.Equal(
        [
            "award Lynet",
            "value dan ingen rigtige", "value bob 7,4 s", "value asger 6,1 s", "value carl 4,1 s", // worst first
            "stamp",
            "award Stimen",
            "value dan 1 i træk", "value carl 3 i træk", "value bob 4 i træk", "value asger 5 i træk",
            "stamp",
            "banner Slutstilling",
            "place dan 4", "place carl 2", "place bob 2", // from last place up
            "banner Og vinderen er …",
            "banner asger!", "place asger 1"
        ], trace);
    }

    [Fact]
    public void Bonus_IsCountedForTheWinners()
    {
        var script = new EndRevealScript(Ended());

        Assert.Equal([(Asger, 1), (Bob, 0), (Carl, 1), (Dan, 0)], script.Ordered.Select(r => (r.Standing.UserId, r.Bonus)));
    }

    [Fact]
    public void SkipToEnd_ShowsTheFinalStandings_FromAnyPoint()
    {
        var script = new EndRevealScript(Ended());
        script.Step();
        script.Step(); // in the middle of Lynet

        script.SkipToEnd();

        Assert.True(script.Done);
        Assert.Null(script.NextWait);
        Assert.All(script.Rows, r => Assert.True(r.Shown));
        Assert.Equal([7, 6, 6, 4], script.Ordered.Select(r => r.Standing.Score));
        Assert.Equal([1, 2, 2, 4], script.Ordered.Select(r => r.Standing.Placement)); // shared 2nd place
    }

    [Fact]
    public void AnAwardWithoutWinners_GivesNoStampAndNoBonus()
    {
        var script = new EndRevealScript(Ended(
        [
            new AwardDto("fastest", [], [new AwardContenderDto(Dan, null, false), new AwardContenderDto(Bob, 9000, false)])
        ]) with { Standings = [new StandingDto(Bob, "bob", 3, 3, 5, 1, []), new StandingDto(Dan, "dan", 1, 1, 5, 2, [])] });

        var trace = Trace(script);

        Assert.DoesNotContain("stamp", trace);
        Assert.Contains("value bob for få rigtige", trace);
        Assert.All(script.Rows, r => Assert.Equal(0, r.Bonus));
    }

    [Fact]
    public void ASharedFirstPlace_RevealsAllWinnersTogether()
    {
        var script = new EndRevealScript(new GameEndedDto(Guid.NewGuid(),
            [new StandingDto(Asger, "asger", 5, 5, 5, 1, []), new StandingDto(Bob, "bob", 5, 5, 5, 1, [])], []));

        var trace = Trace(script);

        Assert.Equal(["banner Slutstilling", "banner Og vinderen er …", "banner asger og bob!", "place asger 1", "place bob 1"], trace);
    }

    // ---- The component on a fake timer ----

    private IRenderedComponent<EndReveal> RenderReveal(Action? onDone = null) =>
        Render<EndReveal>(p => p
            .Add(x => x.Ended, Ended())
            .Add(x => x.Me, Bob)
            .Add(x => x.OnDone, () => onDone?.Invoke())
            .Add(x => x.Actions, "<button data-testid=\"done-action\">Til forsiden</button>"));

    private static string Text(IRenderedComponent<EndReveal> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    [Fact]
    public void Component_PlaysTheWholeReveal_ThenShowsTheButtons()
    {
        var done = 0;
        var cut = RenderReveal(() => done++);
        Assert.Equal("Bonus!", Text(cut, "er-banner"));
        Assert.Empty(cut.FindAll("[data-testid=done-action]"));
        Assert.Equal(["?", "?", "?", "?"], cut.FindAll("[data-testid=placement]").Select(p => p.TextContent));

        _time.Advance(EndRevealScript.BonusIntro);
        cut.WaitForAssertion(() => Assert.Equal("Lynet", Text(cut, "er-award")));

        _time.Advance(TimeSpan.FromSeconds(60)); // the rest, one timer at a time
        for (var i = 0; i < 40 && cut.FindAll("[data-testid=done-action]").Count == 0; i++)
            _time.Advance(TimeSpan.FromSeconds(3));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=done-action]")));
        Assert.Equal("asger!", Text(cut, "er-banner"));
        Assert.Equal(["1", "2", "2", "4"], cut.FindAll("[data-testid=placement]").Select(p => p.TextContent));
        Assert.Contains("(+1 bonus)", cut.Find("[data-user='3'] [data-testid=score]").TextContent);
        Assert.Contains("me", cut.Find("[data-user='2']").ClassList); // my own row is highlighted
        Assert.Equal(1, done);
    }

    [Fact]
    public void Component_SkipButton_ShowsTheResultAtOnce()
    {
        var done = 0;
        var cut = RenderReveal(() => done++);

        cut.Find("[data-testid=skip]").Click();

        Assert.Single(cut.FindAll("[data-testid=done-action]"));
        Assert.Empty(cut.FindAll("[data-testid=skip]"));
        Assert.Equal(["1", "2", "2", "4"], cut.FindAll("[data-testid=placement]").Select(p => p.TextContent));
        Assert.Equal("7 point (+1 bonus)", cut.Find("[data-user='1'] [data-testid=score]").TextContent.Trim());
        Assert.Equal(1, done);
        _time.Advance(TimeSpan.FromMinutes(1)); // no more beats, no second OnDone
        Assert.Equal(1, done);
    }
}
