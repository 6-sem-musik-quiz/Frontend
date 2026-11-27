using Frontend.Domain.DTOs.Maintenance;
using Frontend.Services.Interfaces;
using Frontend.Web.Maintenance;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Frontend.Web.Tests;

// SCRUM-174/176: the maintenance flags for tests. Every service is on, unless listed in "off".
public static class MaintenanceFakes
{
    public static readonly string[] AllServices =
        ["userservice", "lobbyservice", "gameservice", "musicservice", "statisticservice"];

    public static IReadOnlyList<MaintenanceStatusDto> Statuses(params string[] off) =>
        AllServices.Select(s => new MaintenanceStatusDto(s, MaintenanceState.Features[s].Feature, off.Contains(s),
            off.Contains(s) ? null : null, off.Contains(s) ? "admin" : null, null)).ToList();

    // Registers the maintenance API (a substitute), MaintenanceState and ToastService.
    public static IMaintenanceApiClient AddMaintenanceFakes(this IServiceCollection services, params string[] off)
    {
        var api = Substitute.For<IMaintenanceApiClient>();
        api.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Statuses(off));
        services.TryAddSingleton(api);
        services.TryAddSingleton(sp => new MaintenanceState(
            sp.GetRequiredService<IMaintenanceApiClient>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            NullLogger<MaintenanceState>.Instance));
        services.TryAddSingleton(sp => new ToastService(sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddSingleton<Frontend.Web.Auth.UserSession>(); // the login page checks the role (SCRUM-177)
        return api;
    }
}
