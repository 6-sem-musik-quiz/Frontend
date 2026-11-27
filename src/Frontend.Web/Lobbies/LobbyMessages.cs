using System.Net;

using Frontend.Domain.Localization;
using Frontend.Services.Errors;

namespace Frontend.Web.Lobbies;

// LobbyService answers in English ProblemDetails. These are the texts the player can run into,
// in the player's language. Anything unknown returns null, so ApiCallHandler shows the server's own message.
public static class LobbyMessages
{
    private static readonly Dictionary<string, (string Da, string En)> ByDetail = new(StringComparer.OrdinalIgnoreCase)
    {
        ["The lobby is full."] = ("Lobbyen er fuld.", "The lobby is full."),
        ["The game has already started."] = ("Spillet er allerede startet.", "The game has already started."),
        ["You are already in a lobby."] = ("Du er allerede i en lobby. Gå til den eller forlad den øverst på siden.",
            "You are already in a lobby. Go to it or leave it at the top of the page."),
        ["You are already in another lobby."] = ("Du er allerede i en anden lobby. Gå til den eller forlad den øverst på siden.",
            "You are already in another lobby. Go to it or leave it at the top of the page."),
        ["The lobby is busy. Please try again."] = ("Lobbyen er optaget lige nu. Prøv igen.", "The lobby is busy right now. Try again."),
        ["Not all players are ready."] = ("Ikke alle spillere er klar.", "Not all players are ready."),
        ["The players changed while the game was starting. Please try again."] = ("Spillerne ændrede sig, mens spillet startede. Prøv igen.",
            "The players changed while the game was starting. Try again."),
        ["You cannot leave a lobby while the game is running."] = ("Du kan ikke forlade lobbyen, mens spillet kører.",
            "You cannot leave the lobby while the game is running."),
    };

    public static string? For(ApiException ex)
    {
        if (ex.Problem?.Detail is { } detail && ByDetail.TryGetValue(detail.Trim(), out var text))
            return Lang.T(text.Da, text.En);

        return ex.StatusCode switch
        {
            HttpStatusCode.NotFound when ex.Problem?.Detail?.StartsWith("No lobby has the code", StringComparison.Ordinal) == true
                => Lang.T("Ingen lobby har den kode.", "No lobby has that code."),
            HttpStatusCode.NotFound => Lang.T("Lobbyen findes ikke (længere).", "The lobby does not exist (any more)."),
            HttpStatusCode.Forbidden => Lang.T("Kun værten kan starte spillet.", "Only the host can start the game."),
            HttpStatusCode.BadGateway => Lang.T("Spillet kunne ikke startes, fordi spilservicen ikke svarede. Prøv igen.",
                "The game could not be started because the game service did not answer. Try again."),
            _ => null
        };
    }
}
