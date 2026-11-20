using Frontend.Services.Errors;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Auth;

// Fælles fejlhåndtering for sider:
// - ProblemDetails fra services vises som læsbar besked
// - 401 logger ud og sender til /login
// - andre fejl giver en generel fejlbesked
public sealed class ApiCallHandler(IAuthService authService, NavigationManager navigation, ILogger<ApiCallHandler> logger)
{
    public const string GeneralError = "Der opstod en uventet fejl. Prøv igen om lidt.";
    public const string UnreachableError = "Kunne ikke kontakte servicen. Prøv igen om lidt.";

    // Returnerer en fejlbesked til visning, eller null hvis kaldet lykkedes.
    // unauthorizedMessage bruges fx på login-siden, hvor 401 betyder forkert adgangskode: så vises
    // beskeden i stedet for at sende brugeren til /login.
    public async Task<string?> RunAsync(Func<Task> action, string? unauthorizedMessage = null)
    {
        try
        {
            await action();
            return null;
        }
        catch (UnauthorizedApiException) when (unauthorizedMessage is not null)
        {
            return unauthorizedMessage;
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
