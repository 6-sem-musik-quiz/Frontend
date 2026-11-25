using Frontend.Domain.DTOs.Lobbies;
using Frontend.Web.Sound;

namespace Frontend.Web.Lobbies;

// SCRUM-162: which sounds a change in the lobby makes. The lobby page compares the lobby it showed
// with the new one (from the hub, a REST answer or polling), so every player hears the same thing,
// not only the one who acted. Each kind of sound plays at most once per change, so five joins at
// once are one join sound.
public static class LobbySounds
{
    // Returns nothing for the first lobby the page shows (`before` is null) or another lobby.
    public static IReadOnlyList<Sfx> For(LobbyDto? before, LobbyDto after)
    {
        if (before is null || before.Id != after.Id)
            return [];

        var sounds = new List<Sfx>();
        var was = before.Players.ToDictionary(p => p.UserId);
        var now = after.Players.ToDictionary(p => p.UserId);

        var joined = after.Players.Any(p => !was.ContainsKey(p.UserId));
        var left = before.Players.Any(p => !now.ContainsKey(p.UserId));
        var becameFull = after.Players.Count >= after.Settings.MaxPlayers && before.Players.Count < after.Settings.MaxPlayers;

        // The last seat taken gets the full sound instead of the join sound.
        if (becameFull)
            sounds.Add(Sfx.LobbyFull);
        else if (joined)
            sounds.Add(Sfx.LobbyJoin);
        if (left)
            sounds.Add(Sfx.LobbyLeave);

        // Ready changes of players who were there both before and after.
        var stayed = after.Players.Where(p => was.ContainsKey(p.UserId)).ToList();
        var gotReady = stayed.Any(p => p.IsReady && !was[p.UserId].IsReady);
        var gotUnready = stayed.Any(p => !p.IsReady && was[p.UserId].IsReady);

        // Everyone ready (and enough players to start) replaces the single ready stamp.
        if (AllReady(after) && !AllReady(before))
            sounds.Add(Sfx.AllReady);
        else if (gotReady)
            sounds.Add(Sfx.Ready);
        if (gotUnready)
            sounds.Add(Sfx.Unready);

        return sounds;
    }

    private static bool AllReady(LobbyDto lobby) =>
        lobby.Players.Count >= 2 && lobby.Players.All(p => p.IsReady);
}
