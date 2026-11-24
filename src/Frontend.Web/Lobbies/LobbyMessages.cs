using System.Net;

using Frontend.Services.Errors;

namespace Frontend.Web.Lobbies;

// LobbyService answers in English ProblemDetails. These are the texts the player can run into,
// in Danish. Anything unknown returns null, so ApiCallHandler shows the server's own message.
public static class LobbyMessages
{
    private static readonly Dictionary<string, string> ByDetail = new(StringComparer.OrdinalIgnoreCase)
    {
        ["The lobby is full."] = "Lobbyen er fuld.",
        ["The game has already started."] = "Spillet er allerede startet.",
        ["You are already in a lobby."] = "Du er allerede i en lobby. Forlad den først.",
        ["You are already in another lobby."] = "Du er allerede i en anden lobby. Forlad den først.",
        ["The lobby is busy. Please try again."] = "Lobbyen er optaget lige nu. Prøv igen.",
        ["Not all players are ready."] = "Ikke alle spillere er klar.",
        ["The players changed while the game was starting. Please try again."] = "Spillerne ændrede sig, mens spillet startede. Prøv igen.",
        ["You cannot leave a lobby while the game is running."] = "Du kan ikke forlade lobbyen, mens spillet kører.",
    };

    public static string? For(ApiException ex)
    {
        if (ex.Problem?.Detail is { } detail && ByDetail.TryGetValue(detail.Trim(), out var text))
            return text;

        return ex.StatusCode switch
        {
            HttpStatusCode.NotFound when ex.Problem?.Detail?.StartsWith("No lobby has the code", StringComparison.Ordinal) == true
                => "Ingen lobby har den kode.",
            HttpStatusCode.NotFound => "Lobbyen findes ikke (længere).",
            HttpStatusCode.Forbidden => "Kun værten kan starte spillet.",
            HttpStatusCode.BadGateway => "Spillet kunne ikke startes, fordi spilservicen ikke svarede. Prøv igen.",
            _ => null
        };
    }
}
