using Bunit;

using Frontend.Domain.Localization;
using Frontend.Services.Errors;
using Frontend.Web.Components.Game;
using Frontend.Web.Components.Layout;
using Frontend.Web.Components.Statistics;
using Frontend.Web.Games;
using Frontend.Web.Lobbies;
using Frontend.Web.Localization;

using Microsoft.Extensions.DependencyInjection;

namespace Frontend.Web.Tests;

// Lang is static (one user per browser tab), so tests that switch it run alone, never next to the
// other test classes that expect Danish.
[CollectionDefinition(Name, DisableParallelization = true)]
public class LanguageCollection
{
    public const string Name = "Language";
}

// The Danish / English flags in the top bar.
[Collection(LanguageCollection.Name)]
public class LanguageTests : BunitContext
{
    public LanguageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // localStorage and <html lang>
        Services.AddSingleton<LanguageStore>();
        Lang.Set(Language.Danish);
    }

    // Every test leaves the app in Danish for the rest of the suite.
    private sealed class BackToDanish : IDisposable
    {
        public void Dispose() => Lang.Set(Language.Danish);
    }

    [Fact]
    public void ClickingTheBritishFlag_SwitchesLiveComponentsToEnglish_AndRemembersIt()
    {
        using var _ = new BackToDanish();
        var countdown = Render<RoundCountdown>(p => p.Add(c => c.Round, 2).Add(c => c.TotalRounds, 10).Add(c => c.StartsIn, 3));
        var toggle = Render<LanguageToggle>();
        Assert.Contains("Gør dig klar", countdown.Markup);
        Assert.Contains("Runde 2 af 10", countdown.Markup);

        toggle.Find("[data-testid=lang-en]").Click();

        Assert.Equal(Language.English, Lang.Current);
        countdown.WaitForAssertion(() => Assert.Contains("Get ready", countdown.Markup));
        Assert.Contains("Round 2 of 10", countdown.Markup);
        Assert.Equal("true", toggle.Find("[data-testid=lang-en]").GetAttribute("aria-pressed"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem"
                                                    && Equals(i.Arguments[0], LanguageStore.Key) && Equals(i.Arguments[1], "en"));

        toggle.Find("[data-testid=lang-da]").Click();
        countdown.WaitForAssertion(() => Assert.Contains("Gør dig klar", countdown.Markup));
    }

    [Fact]
    public async Task TheStoredLanguage_IsLoadedAtStartup()
    {
        using var _ = new BackToDanish();
        JSInterop.Setup<string?>("localStorage.getItem", LanguageStore.Key).SetResult("en");

        await Services.GetRequiredService<LanguageStore>().LoadAsync();

        Assert.Equal(Language.English, Lang.Current);
    }

    [Fact]
    public void NumbersAndDates_FollowTheLanguage()
    {
        using var _ = new BackToDanish();
        var start = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal("75,5 %", StatsFormat.Percent(0.755));
        Assert.Equal("Uge 48 · 23.–29. november", StatsFormat.WeekTitle(start, start.AddDays(6)));
        Assert.Equal("1. · 4,2 s", RivalTable.StampText(new RivalStamp(1, 4200)));

        Lang.Set(Language.English);

        Assert.Equal("75.5%", StatsFormat.Percent(0.755));
        Assert.Equal("Week 48 · 23–29 November", StatsFormat.WeekTitle(start, start.AddDays(6)));
        Assert.Equal("Week 49 · 30 November – 6 December", StatsFormat.WeekTitle(start.AddDays(7), start.AddDays(13)));
        Assert.Equal("3 h 25 min", StatsFormat.PlayTime(12300));
        Assert.Equal("1. · 4.2 s", RivalTable.StampText(new RivalStamp(1, 4200)));
    }

    [Fact]
    public void ServerErrors_AreShownInTheChosenLanguage()
    {
        using var _ = new BackToDanish();
        Lang.Set(Language.English);
        var full = new ApiException(System.Net.HttpStatusCode.Conflict, "x",
            new Frontend.Domain.DTOs.ProblemDetailsDto { Detail = "The lobby is full." });

        Assert.Equal("The lobby is full.", LobbyMessages.For(full));
        Assert.Equal("You have already answered.", GameMessages.For("HubException: You have already answered."));
        Assert.Equal("Something went wrong in the game. Try again.", GameMessages.General);
    }
}
