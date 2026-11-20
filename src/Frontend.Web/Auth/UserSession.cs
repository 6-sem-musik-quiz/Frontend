namespace Frontend.Web.Auth;

// In-memory kopi af den aktuelle brugers token. Singleton = én pr. browserfane (WebAssembly).
// BearerTokenHandler læser herfra synkront, så HTTP-kald ikke kræver JS interop.
public sealed class UserSession
{
    public ParsedToken? Current { get; private set; }

    // True, når vi har forsøgt at hente fra sessionStorage.
    public bool IsLoaded { get; private set; }

    public void Set(ParsedToken? token)
    {
        Current = token;
        IsLoaded = true;
    }

    public void Clear() => Set(null);
}
