using Frontend.Services.ApiClients;
using Frontend.Services.Auth;
using Frontend.Services.Http;
using Frontend.Services.Interfaces;
using Frontend.Services.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frontend.Services.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceUrlOptions>()
            .Bind(configuration.GetSection(ServiceUrlOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Værten (Frontend.Web) registrerer den rigtige provider; fallback giver anonyme kald.
        services.TryAddSingleton<IAccessTokenProvider, AnonymousAccessTokenProvider>();
        services.AddTransient<BearerTokenHandler>();

        services.AddApiClient<IUserApiClient, UserApiClient>(o => o.UserService);
        services.AddApiClient<ILobbyApiClient, LobbyApiClient>(o => o.LobbyService);
        services.AddApiClient<IGameApiClient, GameApiClient>(o => o.GameService);
        services.AddApiClient<IMusicApiClient, MusicApiClient>(o => o.MusicService);
        services.AddApiClient<IStatisticApiClient, StatisticApiClient>(o => o.StatisticService);

        return services;
    }

    private static void AddApiClient<TClient, TImplementation>(
        this IServiceCollection services, Func<ServiceUrlOptions, Uri> baseAddress)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddHttpClient<TClient, TImplementation>((sp, client) =>
                client.BaseAddress = baseAddress(sp.GetRequiredService<IOptions<ServiceUrlOptions>>().Value))
            .AddHttpMessageHandler<BearerTokenHandler>();
    }
}
