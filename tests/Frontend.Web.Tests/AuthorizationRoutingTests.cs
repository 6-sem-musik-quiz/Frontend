using Bunit;
using Bunit.TestDoubles;

using Frontend.Web.Auth;
using Frontend.Web.Components;
using Frontend.Web.Components.Layout;
using Frontend.Web.Sound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Frontend.Web.Tests;

// Renderer hele Routes-komponenten (AuthorizeRouteView + layout) med en test-autorisationskontekst.
public class AuthorizationRoutingTests : BunitContext
{
    private readonly BunitAuthorizationContext _auth;
    private readonly NavigationManager _navigation;

    public AuthorizationRoutingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // FocusOnNavigate bruger JS
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddScoped<ApiCallHandler>();
        Services.AddSingleton<ISoundEffects>(new FakeSoundEffects()); // MainLayout installs the UI sounds
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()); // /admin reads Grafana:DashboardUrl
        Services.AddSingleton(Substitute.For<Frontend.Services.Interfaces.IMusicApiClient>()); // /admin and / list playlists
        Services.AddSingleton(Substitute.For<Frontend.Services.Interfaces.ILobbyApiClient>()); // / lists lobbies
        Services.AddSingleton(Substitute.For<Frontend.Services.Interfaces.IStatisticApiClient>()); // / shows the leaderboard

        _auth = AddAuthorization();

        // bUnit leverer brugeren (AuthenticationStateProvider), men selve [Authorize]-evalueringen
        // skal ske med ASP.NET's rigtige services, så kombinerede attributter vurderes som i produktion.
        Services.AddSingleton<IAuthorizationService, DefaultAuthorizationService>();
        Services.AddSingleton<IAuthorizationPolicyProvider, DefaultAuthorizationPolicyProvider>();
        Services.AddSingleton<IAuthorizationHandlerProvider, DefaultAuthorizationHandlerProvider>();
        Services.AddSingleton<IAuthorizationHandler, PassThroughAuthorizationHandler>();
        Services.AddSingleton<IAuthorizationEvaluator, DefaultAuthorizationEvaluator>();
        Services.AddSingleton<IAuthorizationHandlerContextFactory, DefaultAuthorizationHandlerContextFactory>();
        Services.AddOptions<AuthorizationOptions>();

        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    [Fact]
    public void Player_OnHome_SeesHomePage()
    {
        _auth.SetAuthorized("spiller");
        _auth.SetRoles("Player");
        _navigation.NavigateTo("/");

        var cut = Render<Routes>();

        Assert.Equal("http://localhost/", _navigation.Uri);
        Assert.Equal("Musikquiz", cut.Find("h1").TextContent);
    }

    [Fact]
    public void AnonymousUser_IsRedirectedToLogin()
    {
        _navigation.NavigateTo("/");

        Render<Routes>();

        Assert.Equal("http://localhost/login?returnUrl=%2F", _navigation.Uri);
    }

    [Fact]
    public void AnonymousUser_OnAdmin_IsRedirectedToLogin_WithReturnUrl()
    {
        _navigation.NavigateTo("/admin");

        Render<Routes>();

        Assert.Equal("http://localhost/login?returnUrl=%2Fadmin", _navigation.Uri);
    }

    [Fact]
    public void Player_OnAdmin_SeesNoAccess()
    {
        _auth.SetAuthorized("spiller");
        _auth.SetRoles("Player");
        _navigation.NavigateTo("/admin");

        var cut = Render<Routes>();

        Assert.Contains("Ingen adgang", cut.Markup);
        Assert.Equal("http://localhost/admin", _navigation.Uri);
    }

    [Fact]
    public void Admin_OnAdmin_SeesAdminPage()
    {
        _auth.SetAuthorized("chef");
        _auth.SetRoles("Admin");
        _navigation.NavigateTo("/admin");

        var cut = Render<Routes>();

        Assert.DoesNotContain("Ingen adgang", cut.Markup);
        Assert.Equal("Playlister", cut.Find("h1").TextContent);
    }

    [Fact]
    public void AnonymousUser_CanOpenLoginAndOpret()
    {
        _navigation.NavigateTo("/opret");

        var cut = Render<Routes>();

        Assert.Equal("http://localhost/opret", _navigation.Uri);
        Assert.Equal("Opret bruger", cut.Find("h1").TextContent);
    }

    [Fact]
    public void TopBar_ShowsUserNameAndLogout_AndAdminLinkOnlyForAdmin()
    {
        _auth.SetAuthorized("spiller");
        _auth.SetRoles("Player");

        var player = Render<TopBar>();

        Assert.Equal("spiller", player.Find("[data-testid=current-user]").TextContent);
        Assert.Contains("Log ud", player.Markup);
        Assert.Empty(player.FindAll("a[href='admin']"));

        _auth.SetRoles("Admin");
        var admin = Render<TopBar>();

        Assert.Single(admin.FindAll("a[href='admin']"));
    }
}
