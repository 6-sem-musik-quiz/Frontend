using System.Text.Json;

using Microsoft.JSInterop;

namespace Frontend.Web.Auth;

// Tokens og bruger, som de gemmes i browserens sessionStorage (ryddes, når fanen lukkes).
// RefreshToken er null for sessioner gemt før refresh tokens (de logges ud, når adgangstokenet udløber).
public sealed record StoredSession(string Token, string? UserName, string? Role, string? RefreshToken = null);

public interface ITokenStorage
{
    Task<StoredSession?> GetAsync();
    Task SetAsync(StoredSession session);
    Task DeleteAsync();
}

// sessionStorage via JS interop. Tokenet er ikke krypteret (det kan en app i browseren ikke),
// men det er en signeret JWT, som backend-services validerer; frontenden bruger kun claims til visning.
public sealed class BrowserSessionTokenStorage(IJSRuntime js) : ITokenStorage
{
    private const string Key = "slopster.session";

    public async Task<StoredSession?> GetAsync()
    {
        var json = await js.InvokeAsync<string?>("sessionStorage.getItem", Key);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<StoredSession>(json);
    }

    public async Task SetAsync(StoredSession session) =>
        await js.InvokeVoidAsync("sessionStorage.setItem", Key, JsonSerializer.Serialize(session));

    public async Task DeleteAsync() => await js.InvokeVoidAsync("sessionStorage.removeItem", Key);
}
