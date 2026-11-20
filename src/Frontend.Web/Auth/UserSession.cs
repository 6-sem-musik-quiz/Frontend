namespace Frontend.Web.Auth;

// In-memory kopi af den aktuelle brugers token. Scoped = én pr. Blazor-circuit (bruger/fane).
// BearerTokenHandler læser herfra synkront, så HTTP-kald ikke kræver JS interop.
public sealed class UserSession
{
    public ParsedToken? Current { get; private set; }

    // True, når vi har forsøgt at hente fra session storage i denne circuit.
    public bool IsLoaded { get; private set; }

    public void Set(ParsedToken? token)
    {
        Current = token;
        IsLoaded = true;
    }

    public void Clear() => Set(null);
}
