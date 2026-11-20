using Frontend.Services.Auth;

using Microsoft.AspNetCore.Components.Authorization;

namespace Frontend.Web.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddFrontendAuth(this IServiceCollection services)
    {
        // [Authorize] håndhæves af AuthorizeRouteView i Routes.razor. Backend-services validerer selve JWT'en.
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        services.AddSingleton(TimeProvider.System);

        // Singleton: i WebAssembly er der én bruger pr. fane, og IHttpClientFactory opretter
        // BearerTokenHandler i sit eget DI-scope, så et scoped UserSession ville være en anden instans.
        services.AddSingleton<UserSession>();
        services.AddSingleton<IAccessTokenProvider, SessionAccessTokenProvider>();

        services.AddScoped<ITokenStorage, BrowserSessionTokenStorage>();
        services.AddScoped<JwtAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ApiCallHandler>();

        return services;
    }
}

// Henter token fra UserSession. Udløbne tokens sendes ikke med.
public sealed class SessionAccessTokenProvider(UserSession session, TimeProvider timeProvider) : IAccessTokenProvider
{
    public string? GetAccessToken()
    {
        var token = session.Current;
        return token is null || token.IsExpired(timeProvider.GetUtcNow()) ? null : token.Token;
    }
}
