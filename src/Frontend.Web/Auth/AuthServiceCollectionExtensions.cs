using Frontend.Services.Auth;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Frontend.Web.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddFrontendAuth(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();

        // Login-state lever i Blazor-circuit'en (ikke i en cookie), så server-endpointet må ikke
        // challenge/forbid. [Authorize] håndhæves af AuthorizeRouteView i Routes.razor.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PassThroughAuthorizationMiddlewareResultHandler>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<UserSession>();
        services.AddScoped<ITokenStorage, ProtectedSessionTokenStorage>();
        services.AddScoped<JwtAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ApiCallHandler>();

        // Giver BearerTokenHandler adgang til den aktuelle circuits token.
        services.AddSingleton<CircuitServicesAccessor>();
        services.AddScoped<CircuitHandler, ServicesAccessorCircuitHandler>();
        services.AddSingleton<IAccessTokenProvider, CircuitAccessTokenProvider>();

        return services;
    }
}

public sealed class PassThroughAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult) => next(context);
}
