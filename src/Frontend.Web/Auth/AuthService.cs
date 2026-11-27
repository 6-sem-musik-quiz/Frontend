using Frontend.Domain.DTOs.Users;
using Frontend.Services.Interfaces;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Auth;

public interface IAuthService
{
    Task LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task RegisterAsync(CreateUserRequest request, CancellationToken ct = default);
    Task LogoutAsync(string? returnUrl = null);
    // Signs out and stays on the page (SCRUM-177: a player who logged in while UserService is off).
    Task SignOutHereAsync();
}

public sealed class AuthService(
    IUserApiClient userApi,
    JwtAuthenticationStateProvider authState,
    UserSession session,
    NavigationManager navigation,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await userApi.LoginAsync(request, ct);
        await authState.SignInAsync(response.AccessToken, response.RefreshToken);
    }

    public async Task RegisterAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        await userApi.CreateUserAsync(request, ct);
        await LoginAsync(new LoginRequest { UserName = request.UserName, Password = request.Password }, ct);
    }

    public async Task LogoutAsync(string? returnUrl = null)
    {
        // Spær refresh-tokenet i UserService. Lykkes det ikke (fx netværk), logges brugeren alligevel ud
        // her; tokenet udløber så af sig selv efter 7 dage.
        await SignOutHereAsync();
        navigation.NavigateTo(LoginUrl.For(returnUrl));
    }

    public async Task SignOutHereAsync()
    {
        if (session.RefreshToken is { } refreshToken)
        {
            try
            {
                await userApi.LogoutAsync(refreshToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation(ex, "Kunne ikke spærre refresh-tokenet ved logout.");
            }
        }
        await authState.SignOutAsync();
    }
}

public static class LoginUrl
{
    public const string Path = "/login";

    public static string For(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) || returnUrl.TrimStart('/').StartsWith("login", StringComparison.OrdinalIgnoreCase)
            ? Path
            : $"{Path}?returnUrl={Uri.EscapeDataString(returnUrl)}";

    // Kun lokale stier accepteres som returnUrl, så login ikke kan bruges til open redirect.
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
