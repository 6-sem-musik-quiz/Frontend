using System.Security.Claims;
using System.Text.Json;

using Frontend.Services.Errors;
using Frontend.Services.Interfaces;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Web.Auth;

// Login-state for Blazor. Læser "unique_name" og "role" fra JWT'en i sessionStorage.
// Adgangstokenet lever kort (15 min). Et minut før det udløber, henter vi et nyt med refresh-tokenet
// (POST /api/auth/refresh). Afviser UserService refresh-tokenet, eller er der intet, ryddes sessionen,
// når adgangstokenet udløber, og alle AuthorizeViews/AuthorizeRouteView opdateres; AuthorizeRouteView
// sender derefter brugeren til /login (se RedirectToLogin).
public sealed class JwtAuthenticationStateProvider(
    UserSession session,
    ITokenStorage storage,
    TimeProvider timeProvider,
    ILogger<JwtAuthenticationStateProvider> logger,
    IUserApiClient? userApi = null) : AuthenticationStateProvider, IDisposable
{
    // Hvor længe før udløb vi henter et nyt adgangstoken.
    public static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);

    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private ITimer? _timer;

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
            // Fx var fanen lukket eller i baggrunden, så timeren ikke nåede at forny. Prøv refresh-tokenet først.
            if (!await TryRefreshAsync(token))
            {
                await ClearAsync();
                return Anonymous;
            }
            token = session.Current!;
        }

        return new AuthenticationState(token.Principal);
    }

    public async Task SignInAsync(string token, string? refreshToken = null)
    {
        var parsed = JwtTokenParser.TryParse(token);
        if (parsed is null || parsed.IsExpired(timeProvider.GetUtcNow()))
        {
            throw new InvalidOperationException(Frontend.Domain.Localization.Lang.T("Login-tjenesten returnerede et ugyldigt eller udløbet token.", "The login service returned an invalid or expired token."));
        }

        await storage.SetAsync(new StoredSession(parsed.Token, parsed.UserName, parsed.Role, refreshToken));
        Apply(parsed, refreshToken);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(parsed.Principal)));
    }

    public async Task SignOutAsync()
    {
        await ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
    }

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var stored = await storage.GetAsync();
            Apply(JwtTokenParser.TryParse(stored?.Token), stored?.RefreshToken);
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

    // Henter et nyt par tokens med refresh-tokenet. True, hvis sessionen nu har et gyldigt adgangstoken.
    // Kun ét refresh ad gangen: et refresh-token virker én gang, og bruges det to gange, spærrer
    // UserService hele login'et.
    private async Task<bool> TryRefreshAsync(ParsedToken expiring)
    {
        if (userApi is null)
        {
            return false;
        }

        await _refreshLock.WaitAsync();
        try
        {
            // Et andet kald har allerede fornyet tokenet, mens vi ventede.
            if (session.Current is { } current && !ReferenceEquals(current, expiring))
            {
                return !current.IsExpired(timeProvider.GetUtcNow());
            }

            if (session.RefreshToken is not { } refreshToken)
            {
                return false;
            }

            var response = await userApi.RefreshAsync(refreshToken);
            await SignInAsync(response.AccessToken, response.RefreshToken);
            logger.LogDebug("Adgangstoken fornyet for {UserName}.", expiring.UserName);
            return true;
        }
        catch (UnauthorizedApiException)
        {
            logger.LogInformation("Refresh-tokenet for {UserName} blev afvist; brugeren logges ud.", expiring.UserName);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fx netværksfejl. Adgangstokenet gælder, til det udløber; derefter logges brugeren ud.
            logger.LogWarning(ex, "Kunne ikke forny adgangstokenet.");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void Apply(ParsedToken? token, string? refreshToken)
    {
        session.Set(token, refreshToken);
        var canRefresh = refreshToken is not null && userApi is not null;
        Schedule(token, canRefresh ? RefreshBeforeExpiry : TimeSpan.Zero);
    }

    private async Task ClearAsync()
    {
        Apply(null, null);
        try
        {
            await storage.DeleteAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Kunne ikke slette session fra sessionStorage.");
        }
    }

    // Timeren fyrer "early" før udløb. Er early > 0, fornyes tokenet; ellers logges brugeren ud ved udløb.
    private void Schedule(ParsedToken? token, TimeSpan early)
    {
        _timer?.Dispose();
        _timer = null;

        if (token?.ExpiresAt is not { } expiresAt)
        {
            return;
        }

        // Task/Timer kan maks. vente ca. 24 dage; længere tokens tjekkes igen ved næste state-opslag.
        var due = expiresAt - early - timeProvider.GetUtcNow();
        var maxDue = TimeSpan.FromDays(24);
        due = due < TimeSpan.Zero ? TimeSpan.Zero : due > maxDue ? maxDue : due;
        var refresh = early > TimeSpan.Zero;
        _timer = timeProvider.CreateTimer(_ => _ = OnTimerAsync(token, refresh), null, due, Timeout.InfiniteTimeSpan);
    }

    private async Task OnTimerAsync(ParsedToken token, bool refresh)
    {
        if (!ReferenceEquals(session.Current, token))
        {
            return;
        }

        if (refresh)
        {
            if (await TryRefreshAsync(token))
            {
                return;
            }

            // Refresh mislykkedes: behold adgangstokenet, til det udløber.
            if (ReferenceEquals(session.Current, token) && !token.IsExpired(timeProvider.GetUtcNow()))
            {
                Schedule(token, TimeSpan.Zero);
                return;
            }
        }
        else if (!token.IsExpired(timeProvider.GetUtcNow()))
        {
            return;
        }

        if (ReferenceEquals(session.Current, token))
        {
            logger.LogInformation("Token for {UserName} er udløbet; brugeren logges ud.", token.UserName);
            await SignOutAsync();
        }
    }
}
