using Frontend.Domain.DTOs.Lobbies;
using Frontend.Web.Lobbies;
using Frontend.Web.Sound;

namespace Frontend.Web.Tests;

// SCRUM-162: which sounds a change in the lobby makes.
public class LobbySoundsTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static LobbyDto Lobby(int maxPlayers, params (int Id, bool Ready)[] players) =>
        new(Id, null, players.Length > 0 ? players[0].Id : 0, false,
            new LobbySettingsDto(1, "Hits", 30, 10, maxPlayers),
            players.Select(p => new LobbyPlayerDto(p.Id, $"p{p.Id}", p.Ready)).ToList(), "Waiting");

    [Fact]
    public void FirstLobby_OrAnotherLobby_MakesNoSound()
    {
        var lobby = Lobby(4, (1, false));
        Assert.Empty(LobbySounds.For(null, lobby));
        Assert.Empty(LobbySounds.For(lobby with { Id = Guid.NewGuid() }, lobby));
    }

    [Fact]
    public void NoChange_MakesNoSound()
    {
        var lobby = Lobby(4, (1, false), (2, true));
        Assert.Empty(LobbySounds.For(lobby, lobby with { }));
    }

    [Fact]
    public void ManyJoinsAtOnce_AreOneJoinSound()
    {
        var sounds = LobbySounds.For(Lobby(8, (1, false)), Lobby(8, (1, false), (2, false), (3, false), (4, false)));
        Assert.Equal([Sfx.LobbyJoin], sounds);
    }

    [Fact]
    public void PlayerLeaves_PlaysTheLeaveSound()
    {
        Assert.Equal([Sfx.LobbyLeave], LobbySounds.For(Lobby(4, (1, false), (2, false)), Lobby(4, (1, false))));
    }

    [Fact]
    public void LastSeatTaken_PlaysFullInsteadOfJoin()
    {
        Assert.Equal([Sfx.LobbyFull], LobbySounds.For(Lobby(3, (1, false), (2, false)), Lobby(3, (1, false), (2, false), (3, false))));
    }

    [Fact]
    public void ReadyAndNotReady_SoundDifferent()
    {
        Assert.Equal([Sfx.Ready], LobbySounds.For(Lobby(4, (1, false), (2, false), (3, false)), Lobby(4, (1, true), (2, false), (3, false))));
        Assert.Equal([Sfx.Unready], LobbySounds.For(Lobby(4, (1, true), (2, false)), Lobby(4, (1, false), (2, false))));
    }

    [Fact]
    public void LastPlayerGetsReady_PlaysAllReadyInsteadOfReady()
    {
        Assert.Equal([Sfx.AllReady], LobbySounds.For(Lobby(4, (1, true), (2, false)), Lobby(4, (1, true), (2, true))));
    }

    [Fact]
    public void ASinglePlayerReady_IsNotAllReady()
    {
        Assert.Equal([Sfx.Ready], LobbySounds.For(Lobby(4, (1, false)), Lobby(4, (1, true))));
    }

    [Fact]
    public void ReadyPlayerLeaves_LeavingEveryoneReady_PlaysLeaveAndAllReady()
    {
        var sounds = LobbySounds.For(Lobby(4, (1, true), (2, true), (3, false)), Lobby(4, (1, true), (2, true)));
        Assert.Equal([Sfx.LobbyLeave, Sfx.AllReady], sounds);
    }
}

// Records the sounds a page plays instead of playing them.
public sealed class FakeSoundEffects : ISoundEffects
{
    public List<Sfx> Played { get; } = [];
    public double? Volume { get; private set; }
    public bool? Muted { get; private set; }

    public Task PlayAsync(Sfx sound)
    {
        Played.Add(sound);
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        Volume = volume;
        return Task.CompletedTask;
    }

    public Task SetMutedAsync(bool muted)
    {
        Muted = muted;
        return Task.CompletedTask;
    }

    public Task<double> GetVolumeAsync() => Task.FromResult(Volume ?? 0.6);

    public Task<bool> GetMutedAsync() => Task.FromResult(Muted ?? false);

    public bool UiInstalled { get; private set; }

    public Task InstallUiAsync()
    {
        UiInstalled = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
