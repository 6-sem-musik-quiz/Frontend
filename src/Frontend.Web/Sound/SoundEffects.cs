using Microsoft.JSInterop;

namespace Frontend.Web.Sound;

// The sound effects the app can play: wwwroot/sounds/<name>.mp3, played by wwwroot/js/sfx.js.
public enum Sfx
{
    // Lobby (SCRUM-162)
    LobbyJoin,
    LobbyLeave,
    Ready,
    Unready,
    LobbyFull,
    AllReady,
    GameStart,

    // Front page and navigation (SCRUM-163). Click and hover are played by sfx.js itself (InstallUiAsync).

    // Cards (SCRUM-164). Pick up, slide and drop are played by hand.js; lock-in by the game page.
    LockIn,

    // The round
    Tick,
    TimeUp,

    // Results (SCRUM-165)
    Correct,
    Wrong,
    Winner,
    GameOver,

    // The staged end screen (SCRUM-155). Existing recordings with their own volume, see sfx.js.
    BonusIntro,       // "Bonus!" on the stage
    AwardReveal,      // a bonus award comes on: Lynet (fastest) or Stimen (longest streak)
    AwardValue,       // a contender's time or streak turns up
    AwardStamp,       // the +1 bonus point lands on the award's winner ("ka-ching")
    StandingsIntro,   // "Slutstilling"
    PlacementReveal,  // a place in the standings turns over
    WinnerSuspense,   // "Og vinderen er …"; the winner's name then gets Winner, for everybody
}

// The shared sound-effect player (SCRUM-161). Volume and mute are stored in the browser and read by
// the JS module on every play, so a settings menu (SCRUM-166) changes them for every page at once.
// Behind an interface so bUnit tests can record which sounds a page plays.
public interface ISoundEffects : IAsyncDisposable
{
    // Plays the sound, unless the player is muted or the same sound just played. Never throws:
    // a sound that cannot play must not break the page.
    Task PlayAsync(Sfx sound);

    // 0 (silent) to 1 (full). Separate from the round's song (IGameAudio), so effects can stay audible
    // with the music turned down. Stored in the browser.
    Task SetVolumeAsync(double volume);

    // The stored effects volume (0.6 when nothing is stored, or when it cannot be read).
    Task<double> GetVolumeAsync();

    Task SetMutedAsync(bool muted);

    Task<bool> GetMutedAsync();

    // Hover and click sounds on every button and link (one listener for the whole page). Called once
    // by MainLayout; later calls do nothing.
    Task InstallUiAsync();
}

public sealed class SoundEffects(IJSRuntime js) : ISoundEffects
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> Module() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/sfx.js");

    // The JS names are the enum names in camelCase: LobbyJoin -> lobbyJoin.
    private static string JsName(Sfx sound)
    {
        var name = sound.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public Task PlayAsync(Sfx sound) => Safe(async m => await m.InvokeAsync<bool>("play", JsName(sound)));

    public Task SetVolumeAsync(double volume) => Safe(m => m.InvokeVoidAsync("setVolume", Math.Clamp(volume, 0, 1)).AsTask());

    public async Task<double> GetVolumeAsync()
    {
        double volume = 0.6;
        await Safe(async m => volume = await m.InvokeAsync<double>("getVolume"));
        return volume;
    }

    public async Task<bool> GetMutedAsync()
    {
        var muted = false;
        await Safe(async m => muted = await m.InvokeAsync<bool>("getMuted"));
        return muted;
    }

    public Task SetMutedAsync(bool muted) => Safe(m => m.InvokeVoidAsync("setMuted", muted).AsTask());

    public Task InstallUiAsync() => Safe(m => m.InvokeVoidAsync("installUi").AsTask());

    private async Task Safe(Func<IJSObjectReference, Task> call)
    {
        try
        {
            await call(await Module());
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            // No audio (old browser, prerendering, page closing): play nothing.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
        _module = null;
    }
}
