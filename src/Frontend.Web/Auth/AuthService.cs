using Frontend.Domain.DTOs.Users;
using Frontend.Services.Interfaces;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Auth;

public interface IAuthService
{
    Task LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task RegisterAsync(CreateUserRequest request, CancellationToken ct = default);
    Task LogoutAsync(string? returnUrl = null);
}

public sealed class AuthService(
    IUserApiClient userApi,
    JwtAuthenticationStateProvider authState,
    NavigationManager navigation) : IAuthService
{
    public async Task LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await userApi.LoginAsync(request, ct);
        await authState.SignInAsync(response.Token);
    }

    public async Task RegisterAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        await userApi.CreateUserAsync(request, ct);
        await LoginAsync(new LoginRequest { UserName = request.UserName, Password = request.Password }, ct);
    }

    public async Task LogoutAsync(string? returnUrl = null)
    {
        await authState.SignOutAsync();
        navigation.NavigateTo(LoginUrl.For(returnUrl));
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
