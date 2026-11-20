using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Web.Auth;

// Login-state for Blazor. Læser "unique_name" og "role" fra JWT'en i sessionStorage.
// Når tokenet udløber, ryddes sessionen og alle AuthorizeViews/AuthorizeRouteView opdateres;
// AuthorizeRouteView sender derefter brugeren til /login (se RedirectToLogin).
public sealed class JwtAuthenticationStateProvider(
    UserSession session,
    ITokenStorage storage,
    TimeProvider timeProvider,
    ILogger<JwtAuthenticationStateProvider> logger) : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private ITimer? _expiryTimer;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!session.IsLoaded)
        {
            await LoadFromStorageAsync();
        }

        var token = session.Current;
        if (token is null)
        {
            return Anonymous;
        }

        if (token.IsExpired(timeProvider.GetUtcNow()))
        {
            await ClearAsync();
            return Anonymous;
        }

        return new AuthenticationState(token.Principal);
    }

    public async Task SignInAsync(string token)
    {
        var parsed = JwtTokenParser.TryParse(token);
        if (parsed is null || parsed.IsExpired(timeProvider.GetUtcNow()))
        {
            throw new InvalidOperationException("Login-tjenesten returnerede et ugyldigt eller udløbet token.");
        }

        await storage.SetAsync(new StoredSession(parsed.Token, parsed.UserName, parsed.Role));
        Apply(parsed);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(parsed.Principal)));
    }

    public async Task SignOutAsync()
    {
        await ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public void Dispose() => _expiryTimer?.Dispose();

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var stored = await storage.GetAsync();
            Apply(JwtTokenParser.TryParse(stored?.Token));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or TaskCanceledException)
        {
            // JS interop er ikke tilgængelig. Vi behandler brugeren som anonym uden at markere
            // sessionen som indlæst.
            logger.LogDebug(ex, "Kunne ikke læse session fra sessionStorage.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Gemt session kunne ikke læses og slettes.");
            await ClearAsync();
        }
    }

    private void Apply(ParsedToken? token)
    {
        session.Set(token);
        ScheduleExpiry(token);
    }

    private async Task ClearAsync()
    {
        Apply(null);
        try
        {
            await storage.DeleteAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Kunne ikke slette session fra sessionStorage.");
        }
    }

    private void ScheduleExpiry(ParsedToken? token)
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;

        if (token?.ExpiresAt is not { } expiresAt)
        {
            return;
        }

        // Task/Timer kan maks. vente ca. 24 dage; længere tokens tjekkes igen ved næste state-opslag.
        var due = expiresAt - timeProvider.GetUtcNow();
        var maxDue = TimeSpan.FromDays(24);
        due = due < TimeSpan.Zero ? TimeSpan.Zero : due > maxDue ? maxDue : due;

        _expiryTimer = timeProvider.CreateTimer(_ => _ = OnTokenExpiredAsync(token), null, due, Timeout.InfiniteTimeSpan);
    }

    private async Task OnTokenExpiredAsync(ParsedToken token)
    {
        if (!ReferenceEquals(session.Current, token) || !token.IsExpired(timeProvider.GetUtcNow()))
        {
            return;
        }

        logger.LogInformation("Token for {UserName} er udløbet; brugeren logges ud.", token.UserName);
        await SignOutAsync();
    }
}
