namespace Frontend.Web.Games;

// GameService's hub errors are English HubException messages. These are the ones a player can
// run into, in Danish. Anything unknown gets a general message, never the server's raw text.
public static class GameMessages
{
    public const string General = "Noget gik galt i spillet. Prøv igen.";

    private static readonly Dictionary<string, string> ByMessage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["The game does not exist."] = "Spillet findes ikke (længere).",
        ["You are not a player in this game."] = "Du er ikke med i det her spil.",
        ["That round is not being played."] = "Runden er slut.",
        ["The time is up."] = "Tiden er gået.",
        ["You have already answered."] = "Du har allerede svaret.",
        ["The token has no valid user id."] = "Du skal logge ind igen."
    };

    // `message` is HubException.Message on the client, which SignalR formats as
    // "An unexpected error occurred invoking 'X' on the server. HubException: <server message>".
    public static string For(string? message)
    {
        var server = ServerText(message);
        if (ByMessage.TryGetValue(server, out var text))
            return text;
        if (server.StartsWith("slotIndex must be between", StringComparison.OrdinalIgnoreCase))
            return "Vælg en plads i din tidslinje.";
        if (server.StartsWith("Round must be between", StringComparison.OrdinalIgnoreCase))
            return "Den runde findes ikke i spillet.";
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
