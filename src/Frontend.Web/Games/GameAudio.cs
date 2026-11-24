using Microsoft.JSInterop;

namespace Frontend.Web.Games;

// Downloads and plays the round's song (wwwroot/js/gameAudio.js). Behind an interface so bUnit
// tests of the game page can use a fake instead of a browser.
public interface IGameAudio : IAsyncDisposable
{
    // Downloads the file from the CDN into memory and returns a blob: URL, or null if the download
    // failed (the page can retry, or fall back to streaming the original URL).
    Task<string?> PrefetchAsync(string url);

    // Plays `source` (a blob: URL or the original URL) from offsetSeconds. Returns false if the
    // browser blocked autoplay: show a Play button and call this again from its click.
    Task<bool> PlayAsync(string source, double offsetSeconds);

    Task StopAsync();

    // Frees the memory of a blob: URL from PrefetchAsync. Other URLs are ignored.
    Task RevokeAsync(string blobUrl);

    // 0 (silent) to 1 (full).
    Task SetVolumeAsync(double volume);
}

public sealed class GameAudio(IJSRuntime js) : IGameAudio
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> Module() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/gameAudio.js");

    public async Task<string?> PrefetchAsync(string url) =>
        await (await Module()).InvokeAsync<string?>("prefetch", url);

    public async Task<bool> PlayAsync(string source, double offsetSeconds) =>
        await (await Module()).InvokeAsync<bool>("play", source, offsetSeconds);

    public async Task StopAsync() =>
        await (await Module()).InvokeVoidAsync("stop");

    public async Task RevokeAsync(string blobUrl) =>
        await (await Module()).InvokeVoidAsync("revoke", blobUrl);

    public async Task SetVolumeAsync(double volume) =>
        await (await Module()).InvokeVoidAsync("setVolume", Math.Clamp(volume, 0, 1));

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;
        try
        {
            await _module.InvokeVoidAsync("stop");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
        _module = null;
    }
}
