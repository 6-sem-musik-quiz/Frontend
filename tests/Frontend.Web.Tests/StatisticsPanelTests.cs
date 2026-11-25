using System.Security.Claims;

using Bunit;

using Frontend.Domain.DTOs.Statistics;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Statistics;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Frontend.Web.Tests;

// SCRUM-116: the week's leaderboard and my stats in the front page's leaderboard column.
public class StatisticsPanelTests : BunitContext
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(1));

    private readonly IStatisticApiClient _statistics = Substitute.For<IStatisticApiClient>();

    public StatisticsPanelTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_statistics);
        Services.AddScoped<ApiCallHandler>();
    }

    private static WeeklyLeaderboardDto Week(
        IReadOnlyList<MostWinsEntryDto>? wins = null, IReadOnlyList<WinRateEntryDto>? winRate = null,
        IReadOnlyList<AccuracyEntryDto>? accuracy = null) =>
        new(Monday, Monday.AddDays(7).AddSeconds(-1), wins ?? [], winRate ?? [], accuracy ?? []);

    private static readonly MyStatsDto NoGames = new(0, 0, 0, 0, 0, 0, null, null);

    // ---- formatting ----

    [Theory]
    [InlineData(0.755, "75,5 %")]
    [InlineData(0.6, "60,0 %")]
    [InlineData(1.0, "100,0 %")]
    [InlineData(0.0, "0,0 %")]
    [InlineData(2.0 / 3, "66,7 %")]
    public void Percent_HasOneDecimalAndDanishComma(double fraction, string expected) =>
        Assert.Equal(expected, StatsFormat.Percent(fraction));

    [Theory]
    [InlineData(12300, "3 t 25 min")]
    [InlineData(3600, "1 t 0 min")]
    [InlineData(600, "10 min")]
    [InlineData(59, "0 min")]
    [InlineData(0, "0 min")]
    public void PlayTime_IsHoursAndMinutes(long seconds, string expected) =>
        Assert.Equal(expected, StatsFormat.PlayTime(seconds));

    [Fact]
    public void WeekTitle_ShowsWeekNumberAndDates()
    {
        Assert.Equal("Uge 48 · 23.–29. november", StatsFormat.WeekTitle(Monday, Monday.AddDays(7).AddSeconds(-1)));

        var acrossMonths = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal("Uge 49 · 30. november – 6. december",
            StatsFormat.WeekTitle(acrossMonths, acrossMonths.AddDays(7).AddSeconds(-1)));
    }

    // ---- weekly leaderboard ----

    [Fact]
    public void Leaderboard_HighlightsMyRow()
    {
        var cut = Render<WeeklyLeaderboard>(p => p
            .Add(c => c.Week, Week(wins: [new(1, "alice", 3, 4), new(42, "me", 2, 3), new(7, "bob", 1, 2)]))
            .Add(c => c.MeId, 42));

        var own = cut.Find("[data-testid=own-row]");
        Assert.Contains("me", own.TextContent);
        Assert.Equal("true", own.GetAttribute("aria-current"));
        Assert.Equal(2, cut.FindAll("[data-testid=row]").Count);
        Assert.Equal(["3 sejre", "2 sejre", "1 sejr"], cut.FindAll("[data-testid=value]").Select(v => v.TextContent));
    }

    [Fact]
    public void Leaderboard_NotSignedIn_HighlightsNobody()
    {
        var cut = Render<WeeklyLeaderboard>(p => p.Add(c => c.Week, Week(wins: [new(42, "me", 2, 3)])));

        Assert.Empty(cut.FindAll("[data-testid=own-row]"));
    }

    [Fact]
    public void Leaderboard_PercentTabs_ShowPercentAndTheFiveGameRule()
    {
        var cut = Render<WeeklyLeaderboard>(p => p
            .Add(c => c.Week, Week(winRate: [new(1, "alice", 0.6, 5)], accuracy: [new(2, "bob", 0.755, 6)])));

        Assert.Empty(cut.FindAll("[data-testid=min-games]")); // not under "Flest sejre"

        cut.Find("[data-testid=tab-winrate]").Click();
        Assert.Equal("60,0 %", cut.Find("[data-testid=value]").TextContent);
        Assert.Equal("Kræver mindst 5 spil denne uge.", cut.Find("[data-testid=min-games]").TextContent);

        cut.Find("[data-testid=tab-accuracy]").Click();
        Assert.Equal("75,5 %", cut.Find("[data-testid=value]").TextContent);
        Assert.Contains("bob", cut.Find("[data-testid=row]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=min-games]"));
    }

    [Fact]
    public void Leaderboard_EmptyWeek_SaysSo()
    {
        var cut = Render<WeeklyLeaderboard>(p => p.Add(c => c.Week, Week()));

        Assert.Equal("Ingen sejre denne uge endnu.", cut.Find("[data-testid=no-rows]").TextContent.Trim());
        Assert.Equal("Uge 48 · 23.–29. november", cut.Find("[data-testid=week-title]").TextContent);
    }

    // ---- my stats ----

    [Fact]
    public void MyStats_NoGames_ShowsEmptyState()
    {
        var cut = Render<MyStats>(p => p.Add(c => c.Stats, NoGames));

        Assert.Equal("Ingen spil endnu.", cut.Find("[data-testid=no-games]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=my-stats]"));
    }

    [Fact]
    public void MyStats_ShowsEveryNumber()
    {
        var cut = Render<MyStats>(p => p.Add(c => c.Stats, new MyStatsDto(
            3, 2, 2.0 / 3, 0.5, 30, 12300, new MostPlayedCategoryDto(7, "Rock", 2), new MostCommonFriendDto(2, "Alice", 1))));

        Assert.Equal("3", cut.Find("[data-testid=games]").TextContent);
        Assert.Equal("2", cut.Find("[data-testid=wins]").TextContent);
        Assert.Equal("66,7 %", cut.Find("[data-testid=win-rate]").TextContent);
        Assert.Equal("50,0 %", cut.Find("[data-testid=accuracy]").TextContent);
        Assert.Equal("30", cut.Find("[data-testid=rounds]").TextContent);
        Assert.Equal("3 t 25 min", cut.Find("[data-testid=play-time]").TextContent);
        Assert.Equal("Rock (2 spil)", cut.Find("[data-testid=category]").TextContent);
        Assert.Equal("Alice (1 spil)", cut.Find("[data-testid=friend]").TextContent);
    }

    [Fact]
    public void MyStats_WithoutCategoryOrFriend_LeavesThemOut()
    {
        var cut = Render<MyStats>(p => p.Add(c => c.Stats, new MyStatsDto(1, 0, 0, 0, 5, 300, null, null)));

        Assert.Empty(cut.FindAll("[data-testid=category]"));
        Assert.Empty(cut.FindAll("[data-testid=friend]"));
    }

    // ---- the panel ----

    private IRenderedComponent<StatsPanel> RenderPanel(string? sub, TimeSpan? refresh = null)
    {
        var user = sub is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "jwt"));
        return Render<StatsPanel>(p => p
            .AddCascadingValue(Task.FromResult(new AuthenticationState(user)))
            .Add(c => c.RefreshInterval, refresh ?? TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Panel_LoadsBoth_AndUsesSubForMyRow()
    {
        _statistics.GetWeeklyLeaderboardAsync("current", Arg.Any<CancellationToken>()).Returns(Week(wins: [new(42, "me", 1, 1)]));
        _statistics.GetMyStatsAsync(Arg.Any<CancellationToken>()).Returns(new MyStatsDto(1, 1, 1, 1, 5, 300, null, null));

        var cut = RenderPanel("42");

        cut.WaitForAssertion(() => Assert.Contains("me", cut.Find("[data-testid=own-row]").TextContent));
        Assert.Equal("1", cut.Find("[data-testid=games]").TextContent);
    }

    [Fact]
    public void Panel_RefreshesOnATimer()
    {
        _statistics.GetWeeklyLeaderboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Week());
        _statistics.GetMyStatsAsync(Arg.Any<CancellationToken>()).Returns(NoGames, new MyStatsDto(1, 0, 0, 0, 5, 300, null, null));

        var cut = RenderPanel("42", TimeSpan.FromMilliseconds(50));

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find("[data-testid=games]").TextContent), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Panel_ServiceDown_ShowsError()
    {
        _statistics.GetWeeklyLeaderboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));
        _statistics.GetMyStatsAsync(Arg.Any<CancellationToken>()).Returns(NoGames);

        var cut = RenderPanel("42");

        cut.WaitForAssertion(() => Assert.Contains(ApiCallHandler.UnreachableError, cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=stats-loading]"));
    }
}
