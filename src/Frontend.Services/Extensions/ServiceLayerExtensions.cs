using Frontend.Services.ApiClients;
using Frontend.Services.Interfaces;
using Frontend.Services.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddHttpClient<IExampleApiClient, ExampleApiClient>((sp, client) =>
            client.BaseAddress = sp.GetRequiredService<IOptions<ServiceUrlOptions>>().Value.UserService);

        return services;
    }
}
