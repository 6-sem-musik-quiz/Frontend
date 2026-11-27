using Frontend.Domain.Localization;

namespace Frontend.Web.Games;

// GameService's hub errors are English HubException messages. These are the ones a player can
// run into, in the player's language. Anything unknown gets a general message, never the server's raw text.
public static class GameMessages
{
    public static string General => Lang.T("Noget gik galt i spillet. Prøv igen.", "Something went wrong in the game. Try again.");

    private static readonly Dictionary<string, (string Da, string En)> ByMessage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["The game does not exist."] = ("Spillet findes ikke (længere).", "The game does not exist (any more)."),
        ["You are not a player in this game."] = ("Du er ikke med i det her spil.", "You are not in this game."),
        ["That round is not being played."] = ("Runden er slut.", "The round is over."),
        ["The time is up."] = ("Tiden er gået.", "Time is up."),
        ["You have already answered."] = ("Du har allerede svaret.", "You have already answered."),
        ["The token has no valid user id."] = ("Du skal logge ind igen.", "You need to log in again.")
    };

    // `message` is HubException.Message on the client, which SignalR formats as
    // "An unexpected error occurred invoking 'X' on the server. HubException: <server message>".
    public static string For(string? message)
    {
        var server = ServerText(message);
        if (ByMessage.TryGetValue(server, out var text))
            return Lang.T(text.Da, text.En);
        if (server.StartsWith("slotIndex must be between", StringComparison.OrdinalIgnoreCase))
            return Lang.T("Vælg en plads i din tidslinje.", "Choose a place in your timeline.");
        if (server.StartsWith("Round must be between", StringComparison.OrdinalIgnoreCase))
            return Lang.T("Den runde findes ikke i spillet.", "That round does not exist in the game.");
        return General;
    }

    private static string ServerText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";
        const string marker = "HubException: ";
        var index = message.LastIndexOf(marker, StringComparison.Ordinal);
        return (index >= 0 ? message[(index + marker.Length)..] : message).Trim();
    }
}
