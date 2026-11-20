using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Frontend.Web.Auth;

// Token og bruger, som de gemmes i browserens session storage (krypteret via Data Protection).
public sealed record StoredSession(string Token, string? UserName, string? Role);

public interface ITokenStorage
{
    Task<StoredSession?> GetAsync();
    Task SetAsync(StoredSession session);
    Task DeleteAsync();
}

// Kræver JS interop, så den kan kun bruges efter circuit'en er startet (ikke under prerender).
public sealed class ProtectedSessionTokenStorage(ProtectedSessionStorage storage) : ITokenStorage
{
    private const string Key = "slopster.session";

    public async Task<StoredSession?> GetAsync()
    {
        var result = await storage.GetAsync<StoredSession>(Key);
        return result.Success ? result.Value : null;
    }

    public async Task SetAsync(StoredSession session) => await storage.SetAsync(Key, session);

    public async Task DeleteAsync() => await storage.DeleteAsync(Key);
}
