using Microsoft.JSInterop;

namespace Frontend.Web.Lobbies;

public interface ILobbyAudio
{
    Task PlayPlayerJoinedAsync();
    Task PlayPlayerLeftAsync();
    Task PlayPlayerReadyAsync();
    Task PlayPlayerNotReadyAsync();
    Task PlayAllReadyAsync();
    Task PlayLobbyFullAsync();
    Task SetVolumeAsync(double volume);
}

public sealed class LobbyAudio(IJSRuntime js) : ILobbyAudio, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> Module() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/lobbyAudio.js");

    public async Task PlayPlayerJoinedAsync() =>
        await (await Module()).InvokeVoidAsync("playPlayerJoined");

    public async Task PlayPlayerLeftAsync() =>
        await (await Module()).InvokeVoidAsync("playPlayerLeft");

    public async Task PlayPlayerReadyAsync() =>
        await (await Module()).InvokeVoidAsync("playPlayerReady");

    public async Task PlayPlayerNotReadyAsync() =>
        await (await Module()).InvokeVoidAsync("playPlayerNotReady");

    public async Task PlayAllReadyAsync() =>
        await (await Module()).InvokeVoidAsync("playAllReady");

    public async Task PlayLobbyFullAsync() =>
        await (await Module()).InvokeVoidAsync("playLobbyFull");

    public async Task SetVolumeAsync(double volume) =>
        await (await Module()).InvokeVoidAsync("setVolume", Math.Clamp(volume, 0, 1));

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
