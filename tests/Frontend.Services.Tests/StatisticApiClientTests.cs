using System.Net;

using Frontend.Domain.DTOs.Statistics;
using Frontend.Services.ApiClients;

namespace Frontend.Services.Tests;

// SCRUM-116: routes against StatisticService, relative to /api/statisticservice/.
public class StatisticApiClientTests
{
    private static (StatisticApiClient Client, StubHttpMessageHandler Stub) Create(string json)
    {
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        return (new StatisticApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/statisticservice/") }), stub);
    }

    [Fact]
    public async Task Weekly_CallsCurrentWeek_AndReadsTheServiceJson()
    {
        // As StatisticService serialises WeeklyLeaderboardResponse.
        var (client, stub) = Create("""
            {"weekStart":"2026-11-23T00:00:00+01:00","weekEnd":"2026-11-29T23:59:59+01:00",
             "mostWins":[{"userId":1,"userName":"alice","wins":3,"gamesPlayed":5}],
             "winRate":[{"userId":1,"userName":"alice","winRate":0.6,"gamesPlayed":5}],
             "accuracy":[{"userId":2,"userName":"bob","accuracy":0.755,"gamesPlayed":6}]}
            """);

        var week = await client.GetWeeklyLeaderboardAsync();

        Assert.Equal("/api/statisticservice/statistics/weekly", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("?week=current", stub.LastRequest.RequestUri.Query);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(1)), week.WeekStart);
        Assert.Equal(new MostWinsEntryDto(1, "alice", 3, 5), week.MostWins.Single());
        Assert.Equal(0.6, week.WinRate.Single().WinRate);
        Assert.Equal(0.755, week.Accuracy.Single().Accuracy);
    }

    [Fact]
    public async Task Me_ReadsStats_WithNullCategoryAndFriend()
    {
        var (client, stub) = Create("""
            {"gamesPlayed":0,"wins":0,"winRate":0,"accuracy":0,"roundsPlayed":0,"timePlayedSeconds":0,
             "mostPlayedCategory":null,"mostCommonFriend":null}
            """);

        var me = await client.GetMyStatsAsync();

        Assert.Equal("/api/statisticservice/statistics/me", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(new MyStatsDto(0, 0, 0, 0, 0, 0, null, null), me);
    }
}
