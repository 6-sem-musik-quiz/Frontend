using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Domain.DTOs.Maintenance;
using Frontend.Domain.DTOs.Users;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Admin;
using Frontend.Web.Components.Maintenance;
using Frontend.Web.Components.Pages;
using Frontend.Web.Maintenance;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

namespace Frontend.Web.Tests;

// SCRUM-174/175/176/177: switching services off ("design to be disabled") in the frontend.
public class MaintenanceTests : BunitContext
{
    private readonly FakeTimeProvider _time = new();
    private readonly IMaintenanceApiClient _api;
    private readonly IAuthService _auth = Substitute.For<IAuthService>();

    public MaintenanceTests()
    {
        Services.AddLogging();
        Services.AddSingleton<TimeProvider>(_time);
        _api = Services.AddMaintenanceFakes();
        Services.AddSingleton(_auth);
        Services.AddScoped<ApiCallHandler>();
    }

    private MaintenanceState State => Services.GetRequiredService<MaintenanceState>();
    private ToastService Toasts => Services.GetRequiredService<ToastService>();

    private async Task SwitchOff(params string[] off)
    {
        _api.GetAllAsync(Arg.Any<CancellationToken>()).Returns(MaintenanceFakes.Statuses(off));
        await State.RefreshAsync();
    }

    private IRenderedComponent<RequiresService> Wrapped(string service, bool replace = false) =>
        Render<RequiresService>(p => p
            .Add(x => x.Service, service)
            .Add(x => x.Replace, replace)
            .Add(x => x.Heading, "Lobbyer")
            .AddChildContent("<button data-testid=\"feature\">Opret</button>"));

    // ---- RequiresService ----

    [Fact]
    public void ServiceOn_TheFeatureWorksNormally()
    {
        var cut = Wrapped("lobbyservice");

        Assert.Empty(cut.FindAll("[data-testid=maintenance-cover]"));
        Assert.DoesNotContain("is-off", cut.Find(".requires-service").ClassList);
    }

    [Fact]
    public async Task ServiceOff_GreysOut_AndAClickShowsTheToast()
    {
        var cut = Wrapped("lobbyservice");

        await cut.InvokeAsync(() => SwitchOff("lobbyservice"));

        Assert.Contains("is-off", cut.Find(".requires-service").ClassList);
        cut.Find("[data-testid=maintenance-cover]").Click();
        Assert.Equal(["Lobbyer er under vedligeholdelse."], Toasts.Toasts.Select(t => t.Text));
    }

    [Fact]
    public async Task SwitchedBackOn_TheFeatureComesBackByItself()
    {
        var cut = Wrapped("lobbyservice");
        await cut.InvokeAsync(() => SwitchOff("lobbyservice"));

        await cut.InvokeAsync(() => SwitchOff());

        Assert.Empty(cut.FindAll("[data-testid=maintenance-cover]"));
        Assert.DoesNotContain("is-off", cut.Find(".requires-service").ClassList);
    }

    [Fact]
    public async Task SeveralServices_AnyOfThemOff_TurnsItOff()
    {
        var cut = Wrapped("lobbyservice,musicservice");

        await cut.InvokeAsync(() => SwitchOff("musicservice"));

        cut.Find("[data-testid=maintenance-cover]").Click();
        Assert.Equal(["Musikkataloget er under vedligeholdelse."], Toasts.Toasts.Select(t => t.Text));
    }

    [Fact]
    public async Task Replace_ShowsANoticeWithTheAdminsMessage_InsteadOfTheFeature()
    {
        var cut = Wrapped("lobbyservice", replace: true);

        _api.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            [new MaintenanceStatusDto("lobbyservice", "Lobby", true, "Tilbage kl. 20.", "asger", null)]);
        await cut.InvokeAsync(State.RefreshAsync);

        Assert.Empty(cut.FindAll("[data-testid=feature]"));
        var notice = cut.Find("[data-testid=maintenance-notice]");
        Assert.Contains("Lobbyer", notice.QuerySelector("h2")!.TextContent);
        Assert.Contains("Lobbyer er under vedligeholdelse. Tilbage kl. 20.", notice.TextContent);
    }

    // ---- 503 from a service ----

    [Fact]
    public async Task A503Maintenance_ShowsTheToast_AndGreysTheFeatureOutAtOnce()
    {
        var cut = Wrapped("gameservice");
        var handler = Services.GetRequiredService<ApiCallHandler>();

        string? error = null;
        await cut.InvokeAsync(async () => error = await handler.RunAsync(() =>
            throw new ServiceInMaintenanceException("gameservice", "Tilbage kl. 20.", new ProblemDetailsDto { Detail = "Tilbage kl. 20." })));

        Assert.Equal("Spillet er under vedligeholdelse. Du kan blive i lobbyen imens. Tilbage kl. 20.", error);
        Assert.Equal([error], Toasts.Toasts.Select(t => t.Text));
        Assert.Contains("is-off", cut.Find(".requires-service").ClassList); // before the next poll
    }

    [Fact]
    public void Toasts_DisappearByThemselves()
    {
        Toasts.Show("Lobbyer er under vedligeholdelse.");
        Toasts.Show("Lobbyer er under vedligeholdelse."); // not twice

        Assert.Single(Toasts.Toasts);
        _time.Advance(ToastService.ShowFor);
        Assert.Empty(Toasts.Toasts);
    }

    // ---- polling ----

    [Fact]
    public async Task State_PollsTheEdge_AndKeepsWhatItKnew_WhenTheEdgeCannotBeAsked()
    {
        await SwitchOff("statisticservice");
        Assert.True(State.IsOff("statisticservice"));

        _api.GetAllAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<MaintenanceStatusDto>>(_ => throw new HttpRequestException());
        await State.RefreshAsync();

        Assert.True(State.IsOff("statisticservice"));
    }

    [Fact]
    public async Task Banner_ListsWhatIsSwitchedOff()
    {
        var cut = Render<MaintenanceBanner>();
        Assert.Empty(cut.FindAll("[data-testid=maintenance-banner]"));

        await cut.InvokeAsync(() => SwitchOff("lobbyservice", "statisticservice"));

        Assert.Contains("Lobbyer, Leaderboard og statistik", cut.Find("[data-testid=maintenance-banner]").TextContent);
    }

    // ---- admin card (SCRUM-175) ----

    [Fact]
    public void AdminCard_HasARowPerService()
    {
        var cut = Render<MaintenanceCard>();

        var rows = cut.FindAll("[data-testid=maintenance-row]");
        Assert.Equal(MaintenanceFakes.AllServices, rows.Select(r => r.GetAttribute("data-service")));
        Assert.All(rows, r => Assert.Equal("Aktiv", r.QuerySelector("[data-testid=status]")!.TextContent));
    }

    [Fact]
    public async Task AdminCard_SwitchingOff_NeedsConfirmation_AndSendsTheMessage()
    {
        var cut = Render<MaintenanceCard>();

        cut.Find("[data-service=lobbyservice] [data-testid=switch-off]").Click();
        Assert.Contains("Brugerne kan ikke bruge lobbyer imens.", cut.Find("[data-testid=confirm-text]").TextContent);
        await _api.DidNotReceiveWithAnyArgs().SwitchAsync(default!, default, default);

        cut.Find("[data-testid=message]").Input("Tilbage kl. 20");
        await cut.Find("[data-testid=confirm]").ClickAsync(new());

        await _api.Received(1).SwitchAsync("lobbyservice", true, "Tilbage kl. 20", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCard_ShowsWhoSwitchedItOff_AndSwitchesOn()
    {
        _api.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            [new MaintenanceStatusDto("gameservice", "Game", true, "Opdatering", "asger", DateTimeOffset.UtcNow)]);
        var cut = Render<MaintenanceCard>();

        var row = cut.Find("[data-service=gameservice]");
        Assert.Equal("Vedligeholdelse", row.QuerySelector("[data-testid=status]")!.TextContent);
        Assert.Contains("asger", row.QuerySelector("[data-testid=off-by]")!.TextContent);
        await cut.Find("[data-service=gameservice] [data-testid=switch-on]").ClickAsync(new());

        await _api.Received(1).SwitchAsync("gameservice", false, null, Arg.Any<CancellationToken>());
    }

    // ---- login while UserService is off (SCRUM-177) ----

    private void LogsInAs(string role) =>
        _auth.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Services.GetRequiredService<UserSession>().Set(JwtTokenParser.TryParse(TestTokens.Create("spiller", role, DateTimeOffset.UtcNow.AddHours(1))));
            return Task.CompletedTask;
        });

    private static void Submit(IRenderedComponent<Login> cut)
    {
        cut.Find("#username").Change("spiller");
        cut.Find("#password").Change("hemmelig1");
        cut.Find("form").Submit();
    }

    [Fact]
    public async Task Login_WhileUserServiceIsOff_APlayerIsSignedOutAgain_WithTheMessage()
    {
        await SwitchOff("userservice");
        LogsInAs("Player");
        var cut = Render<Login>();
        Assert.Contains("Kun administratorer", cut.Find("[data-testid=login-maintenance]").TextContent);
        var before = Services.GetRequiredService<NavigationManager>().Uri;

        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains("Login og oprettelse er under vedligeholdelse.", cut.Markup));
        await _auth.Received(1).SignOutHereAsync();
        Assert.Equal(before, Services.GetRequiredService<NavigationManager>().Uri); // stays on the login page
    }

    [Fact]
    public async Task Login_WhileUserServiceIsOff_AnAdminGetsIn()
    {
        await SwitchOff("userservice");
        LogsInAs("Admin");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login");
        var cut = Render<Login>();

        Submit(cut);

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri));
        await _auth.DidNotReceive().SignOutHereAsync();
    }
}
