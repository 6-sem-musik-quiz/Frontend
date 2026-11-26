using Microsoft.JSInterop;

namespace Frontend.Web.Sound;

// Quiet looping music on the front page and in the lobby (SCRUM-167), played by wwwroot/js/bgMusic.js.
// Its volume is a small part of the "Musik" setting, so the game's song is always far louder.
public interface IBackgroundMusic : IAsyncDisposable
{
    // Plays the music on the pages that have it and fades it out on the others. Never throws.
    Task SetPageAsync(string relativePath);

    // After the "Musik" setting changed (the options menu).
    Task RefreshVolumeAsync();
}

public sealed class BackgroundMusic(IJSRuntime js) : IBackgroundMusic
{
    private IJSObjectReference? _module;

    // The front page and the lobby. Not the game (its own song), login, sign-up or admin.
    public static bool HasMusic(string relativePath)
    {
        var path = relativePath.Split('?', '#')[0].Trim('/');
        return path.Length == 0 || path.StartsWith("lobby/", StringComparison.OrdinalIgnoreCase);
    }

    public Task SetPageAsync(string relativePath) =>
        Safe(m => m.InvokeVoidAsync("setWanted", HasMusic(relativePath)).AsTask());

    public Task RefreshVolumeAsync() => Safe(m => m.InvokeVoidAsync("refreshVolume").AsTask());

    private async Task Safe(Func<IJSObjectReference, Task> call)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/bgMusic.js");
            await call(_module);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            // No audio: play nothing.
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
