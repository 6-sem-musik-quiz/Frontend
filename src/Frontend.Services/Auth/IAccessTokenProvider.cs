namespace Frontend.Services.Auth;

// Giver den aktuelle brugers JWT. Implementeres af Web-laget (læser fra brugerens Blazor-circuit).
public interface IAccessTokenProvider
{
    string? GetAccessToken();
}

// Fallback, når værten ikke har registreret en provider: ingen token.
internal sealed class AnonymousAccessTokenProvider : IAccessTokenProvider
{
    public string? GetAccessToken() => null;
}
