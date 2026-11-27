using System.Net;

using Frontend.Domain.Localization;
using Frontend.Services.Errors;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Auth;

// Fælles fejlhåndtering for sider:
// - ProblemDetails fra services vises som læsbar besked
// - 401 logger ud og sender til /login
// - andre fejl giver en generel fejlbesked
public sealed class ApiCallHandler
{
    private readonly IAuthService authService;
    private readonly NavigationManager navigation;
    private readonly ILogger<ApiCallHandler> logger;
    private readonly Maintenance.MaintenanceState? maintenance;
    private readonly Maintenance.ToastService? toasts;

    public ApiCallHandler(IAuthService authService, NavigationManager navigation, ILogger<ApiCallHandler> logger)
    {
        this.authService = authService;
        this.navigation = navigation;
        this.logger = logger;
    }

    // SCRUM-176: in the app (DI picks the longest constructor it can fill) a maintenance 503 also shows
    // the toast and greys the service's features out at once.
    public ApiCallHandler(IAuthService authService, NavigationManager navigation, ILogger<ApiCallHandler> logger,
        Maintenance.MaintenanceState maintenance, Maintenance.ToastService toasts)
        : this(authService, navigation, logger)
    {
        this.maintenance = maintenance;
        this.toasts = toasts;
    }

    public static string GeneralError => Lang.T("Der opstod en uventet fejl. Prøv igen om lidt.", "An unexpected error occurred. Try again in a moment.");
    public static string UnreachableError => Lang.T("Kunne ikke kontakte servicen. Prøv igen om lidt.", "Could not reach the service. Try again in a moment.");

    // Returnerer en fejlbesked til visning, eller null hvis kaldet lykkedes.
    // unauthorizedMessage bruges fx på login-siden, hvor 401 betyder forkert adgangskode: så vises
    // beskeden i stedet for at sende brugeren til /login.
    // statusMessages gives a page its own text for a status code, e.g. 409 → "Navnet findes allerede".
    // messageFor can pick a text from the whole error (e.g. its detail); null falls through to the rest.
    public async Task<string?> RunAsync(Func<Task> action, string? unauthorizedMessage = null,
        IReadOnlyDictionary<HttpStatusCode, string>? statusMessages = null,
        Func<ApiException, string?>? messageFor = null)
    {
        try
        {
            await action();
            return null;
        }
        catch (ServiceInMaintenanceException ex)
        {
            // SCRUM-176: the same maintenance toast everywhere, not a generic error.
            maintenance?.MarkOff(ex.Service, ex.Problem?.Detail);
            var text = maintenance?.TextFor(ex.Service) ?? Maintenance.MaintenanceState.FallbackText;
            toasts?.Show(text);
            return text;
        }
        catch (UnauthorizedApiException) when (unauthorizedMessage is not null)
        {
            return unauthorizedMessage;
        }
        catch (ApiException ex) when (ex is not UnauthorizedApiException && messageFor?.Invoke(ex) is not null)
        {
            return messageFor!(ex);
        }
        catch (ApiException ex) when (ex is not UnauthorizedApiException
                                      && statusMessages?.TryGetValue(ex.StatusCode, out _) == true)
        {
            return statusMessages[ex.StatusCode];
        }
        catch (UnauthorizedApiException ex)
        {
            logger.LogInformation("Service svarede 401; brugeren sendes til login.");
            await authService.LogoutAsync("/" + navigation.ToBaseRelativePath(navigation.Uri));
            return ex.Message;
        }
        catch (ApiException ex) when (ex.Problem is not null)
        {
            return ex.Message;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Service svarede {StatusCode} uden ProblemDetails.", (int)ex.StatusCode);
            return GeneralError;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Service kunne ikke kontaktes.");
            return UnreachableError;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Uventet fejl ved API-kald.");
            return GeneralError;
        }
    }
}
