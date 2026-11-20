using Frontend.Services.Auth;

using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Frontend.Web.Auth;

// IHttpClientFactory opretter DelegatingHandlers i sit eget DI-scope, så de kan ikke få fat i
// circuit-scoped services direkte. Mønsteret fra Microsofts docs ("Access server-side Blazor
// services from a different DI scope") gemmer circuit'ens IServiceProvider i en AsyncLocal.
public sealed class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> BlazorServices = new();

    public IServiceProvider? Services
    {
        get => BlazorServices.Value;
        set => BlazorServices.Value = value;
    }
}

public sealed class ServicesAccessorCircuitHandler(IServiceProvider services, CircuitServicesAccessor servicesAccessor)
    : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            servicesAccessor.Services = services;
            await next(context);
            servicesAccessor.Services = null;
        };
}

// Henter token fra den aktuelle circuits UserSession. Udløbne tokens sendes ikke med.
public sealed class CircuitAccessTokenProvider(CircuitServicesAccessor accessor, TimeProvider timeProvider)
    : IAccessTokenProvider
{
    public string? GetAccessToken()
    {
        var token = accessor.Services?.GetService<UserSession>()?.Current;
        return token is null || token.IsExpired(timeProvider.GetUtcNow()) ? null : token.Token;
    }
}
