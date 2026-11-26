namespace Frontend.Web.Games;

// SCRUM-168: how far the server's clock is ahead of ours, so RoundStarted.PlaybackStartsAt (server time)
// can be turned into a moment on our clock. Each sample asks the server for its time and assumes the
// answer was read halfway through the round trip; the sample with the shortest round trip is the most
// precise, as it leaves the least room for the network to be lopsided.
public static class ClockSync
{
    public const int Samples = 5;

    // The offset such that our time + offset = server time. Null if the server could not tell.
    public static async Task<TimeSpan?> MeasureAsync(Func<Task<DateTimeOffset>> serverTime, TimeProvider time,
        int samples = Samples)
    {
        TimeSpan? best = null, bestRoundTrip = null;
        for (var i = 0; i < samples; i++)
        {
            var sent = time.GetTimestamp();
            DateTimeOffset server;
            try
            {
                server = await serverTime();
            }
            catch (GameHubException)
            {
                return best; // e.g. an older GameService without ServerTime: fall back to startsInMs
            }
            var roundTrip = time.GetElapsedTime(sent);
            if (bestRoundTrip is null || roundTrip < bestRoundTrip)
            {
                bestRoundTrip = roundTrip;
                best = server + roundTrip / 2 - time.GetUtcNow();
            }
        }
        return best;
    }
}
