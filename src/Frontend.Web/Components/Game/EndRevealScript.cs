using System.Globalization;

using Frontend.Domain.DTOs.Games;

namespace Frontend.Web.Components.Game;

// SCRUM-155: the game's end as a staged reveal, as a list of beats (wait, then change what is shown).
// Pure: EndReveal runs the beats on a timer, tests read them directly. About 20 s for four players.
//   1. "Bonus!" with the standings as after the last round (score without bonus), placements hidden.
//   2. Each award: title and rule; contenders' values one at a time, worst first; the winners get a
//      +1 stamp, their score goes up and the rows slide to their new order.
//   3. "Slutstilling": placements from last place up; "Og vinderen er …" and a pause before 1st.
public sealed class EndRevealScript
{
    public static readonly TimeSpan BonusIntro = TimeSpan.FromMilliseconds(1600);
    public static readonly TimeSpan AwardIntro = TimeSpan.FromMilliseconds(1400);
    public static readonly TimeSpan PerContender = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan BeforeStamp = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan StampToScore = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan AfterAward = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan FinaleIntro = TimeSpan.FromMilliseconds(1400);
    public static readonly TimeSpan PerPlacement = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan BeforeWinner = TimeSpan.FromMilliseconds(2000);

    public sealed record Contender(int UserId, string Name, string Value, bool Qualified)
    {
        public bool Shown { get; set; }
        public bool Won { get; set; }
    }

    public sealed record Award(string Title, string Kicker, string Rule, List<Contender> Contenders, IReadOnlyList<int> Winners)
    {
        public bool Stamped { get; set; }
    }

    public sealed class Row(StandingDto standing, int bonus)
    {
        public StandingDto Standing { get; } = standing;
        public int Bonus { get; } = bonus;               // awards won (already in Standing.Score)
        public int BonusShown { get; set; }              // how much of it is on screen yet
        public int ScoreShown => Standing.Score - Bonus + BonusShown;
        public bool PlacementShown { get; set; }
        public bool Champion { get; set; }
        public bool Gained { get; set; }                 // just got +1: the row flashes
    }

    // What the stage shows: a big banner, or an award.
    public string? Banner { get; private set; } = "Bonus!";
    public Award? Current { get; private set; }
    public bool Done { get; private set; }
    public IReadOnlyList<Row> Rows { get; }
    public IReadOnlyList<(TimeSpan Wait, Action Apply)> Beats { get; }

    // Rows by score on screen (highest first), ties in the server's order.
    public IReadOnlyList<Row> Ordered => Rows
        .Select((r, i) => (r, i))
        .OrderByDescending(x => x.r.ScoreShown)
        .ThenBy(x => x.i)
        .Select(x => x.r)
        .ToList();

    public EndRevealScript(GameEndedDto ended)
    {
        var awards = ended.Awards ?? [];
        Rows = ended.Standings
            .Select(s => new Row(s, awards.Count(a => a.Winners.Contains(s.UserId))))
            .ToList();
        var names = ended.Standings.ToDictionary(s => s.UserId, s => s.UserName);
        var beats = new List<(TimeSpan, Action)>();

        for (var i = 0; i < awards.Count; i++)
            AddAward(beats, ToAward(awards[i], i + 1, awards.Count, names), i == 0 ? BonusIntro : AfterAward);

        // The finale: from last place up, the 1st places last after a pause.
        beats.Add((awards.Count == 0 ? BonusIntro : AfterAward, () => { Current = null; Banner = "Slutstilling"; }));
        var bottomUp = Rows.Select((r, i) => (r, i)).OrderByDescending(x => x.r.Standing.Placement).ThenByDescending(x => x.i)
            .Select(x => x.r).ToList();
        var others = bottomUp.Where(r => r.Standing.Placement != 1).ToList();
        var champions = bottomUp.Where(r => r.Standing.Placement == 1).Reverse().ToList();
        var wait = FinaleIntro;
        foreach (var row in others)
        {
            beats.Add((wait, () => row.PlacementShown = true));
            wait = PerPlacement;
        }
        beats.Add((wait, () => Banner = "Og vinderen er …"));
        beats.Add((BeforeWinner, () =>
        {
            foreach (var row in champions)
            {
                row.PlacementShown = true;
                row.Champion = true;
            }
            Banner = string.Join(" og ", champions.Select(c => c.Standing.UserName)) + "!";
            Done = true;
        }));
        Beats = beats;
    }

    // The beats run in order; Next is the one that has not run yet.
    public int Next { get; private set; }

    // How long to wait before the next beat; null when all have run.
    public TimeSpan? NextWait => Next < Beats.Count ? Beats[Next].Wait : null;

    public void Step()
    {
        if (Next < Beats.Count)
            Beats[Next++].Apply();
    }

    // Runs the beats that are left at once ("Spring til resultatet", or a server without awards).
    public void SkipToEnd()
    {
        while (Next < Beats.Count)
            Step();
        foreach (var row in Rows)
            row.Gained = false;
    }

    private void AddAward(List<(TimeSpan, Action)> beats, Award award, TimeSpan first)
    {
        beats.Add((first, () =>
        {
            Banner = null;
            Current = award;
            foreach (var row in Rows) row.Gained = false;
        }));
        // Worst first, so the best value comes last.
        var wait = AwardIntro;
        foreach (var contender in Enumerable.Reverse(award.Contenders))
        {
            beats.Add((wait, () => contender.Shown = true));
            wait = PerContender;
        }
        beats.Add((BeforeStamp, () =>
        {
            foreach (var c in award.Contenders.Where(c => award.Winners.Contains(c.UserId)))
                c.Won = true;
            award.Stamped = award.Winners.Count > 0;
        }));
        if (award.Winners.Count > 0)
            beats.Add((StampToScore, () =>
            {
                foreach (var row in Rows.Where(r => award.Winners.Contains(r.Standing.UserId)))
                {
                    row.BonusShown++;
                    row.Gained = true;
                }
            }));
    }

    private static Award ToAward(AwardDto dto, int number, int count, IReadOnlyDictionary<int, string> names)
    {
        var fastest = dto.Kind == "fastest";
        var contenders = dto.Contenders
            .Select(c => new Contender(c.UserId, names.GetValueOrDefault(c.UserId, "?"),
                fastest ? FastestValue(c) : $"{c.Value ?? 0} i træk", c.Qualified))
            .ToList();
        return new Award(
            fastest ? "Lynet" : dto.Kind == "streak" ? "Stimen" : dto.Kind,
            $"Bonus {number} af {count}",
            fastest ? "Hurtigst i snit, kun rigtige svar tæller" : "Flest rigtige i træk",
            contenders,
            dto.Winners);
    }

    private static string FastestValue(AwardContenderDto c) =>
        c.Value is not { } ms ? "ingen rigtige"
        : !c.Qualified ? "for få rigtige"
        : (ms / 1000.0).ToString("0.0", CultureInfo.GetCultureInfo("da-DK")) + " s";
}
