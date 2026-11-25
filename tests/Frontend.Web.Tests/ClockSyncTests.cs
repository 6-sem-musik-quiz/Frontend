using Frontend.Web.Games;

using Microsoft.Extensions.Time.Testing;

namespace Frontend.Web.Tests;

// SCRUM-168: the offset to the server's clock, from the sample with the shortest round trip.
public class ClockSyncTests
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(3) + TimeSpan.FromMilliseconds(250); // the server is ahead

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));

    // A call whose request takes `up` and whose reply takes `down`; the server reads its clock in between.
    private Func<Task<DateTimeOffset>> Server(params (int Up, int Down)[] trips)
    {
        var next = 0;
        return () =>
        {
            var (up, down) = trips[next++];
            _time.Advance(TimeSpan.FromMilliseconds(up));
            var server = _time.GetUtcNow() + Skew;
            _time.Advance(TimeSpan.FromMilliseconds(down));
            return Task.FromResult(server);
        };
    }

    [Fact]
    public async Task ASymmetricRoundTrip_GivesTheExactOffset()
    {
        var offset = await ClockSync.MeasureAsync(Server((40, 40), (40, 40), (40, 40), (40, 40), (40, 40)), _time);

        Assert.Equal(Skew, offset);
    }

    [Fact]
    public async Task TheShortestRoundTrip_IsUsed()
    {
        // A slow, lopsided sample would be 150 ms off; the 20 ms one is 2 ms off at most.
        var offset = await ClockSync.MeasureAsync(Server((350, 50), (12, 8), (200, 200), (90, 10), (60, 60)), _time);

        Assert.Equal(Skew + TimeSpan.FromMilliseconds(2), offset); // 12 up, 8 down: read 2 ms after the midpoint
    }

    [Fact]
    public async Task AServerWithoutServerTime_GivesNull()
    {
        var offset = await ClockSync.MeasureAsync(
            () => Task.FromException<DateTimeOffset>(new GameHubException("Ukendt metode.")), _time);

        Assert.Null(offset);
    }
}
