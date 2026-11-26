using Frontend.Domain.DTOs.Games;

namespace Frontend.Web.Components.Game;

// SCRUM-154: the guess map's year axis. It covers every finite end of the answers' ranges and the real
// year, with some room on both sides; ticks every 5, 10 or 20 years depending on the span.
public sealed record GuessAxis(int Min, int Max, IReadOnlyList<int> Ticks)
{
    public const int MinSpan = 10;

    public double Position(int year) => Max == Min ? 0.5 : (double)(year - Min) / (Max - Min);

    public static GuessAxis For(RoundEndedDto ended)
    {
        var years = ended.Results
            .SelectMany(r => r.Range ?? [])
            .OfType<int>()
            .Append(ended.Song.Year)
            .ToList();
        int lo = years.Min(), hi = years.Max();

        // Room on both sides (10 % of the span, at least 3 years), and never shorter than MinSpan.
        var pad = Math.Max(3, (hi - lo) / 10);
        lo -= pad;
        hi += pad;
        if (hi - lo < MinSpan)
        {
            var grow = MinSpan - (hi - lo);
            lo -= grow / 2;
            hi += grow - grow / 2;
        }

        var step = hi - lo > 60 ? 20 : hi - lo > 25 ? 10 : 5;
        var first = (int)Math.Ceiling(lo / (double)step) * step;
        var ticks = new List<int>();
        for (var y = first; y <= hi; y += step)
            ticks.Add(y);
        return new GuessAxis(lo, hi, ticks);
    }
}
