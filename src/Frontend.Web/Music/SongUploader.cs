using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Frontend.Domain.DTOs.Music;
using Frontend.Services.Auth;
using Frontend.Services.Http;
using Frontend.Services.Options;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Frontend.Web.Music;

// Uploads an mp3 to MusicService: POST /api/musicservice/playlists/{id}/songs (SCRUM-93).
public interface ISongUploader
{
    // fileInput is the <input type="file"> (InputFile.Element) with the selected mp3.
    // onProgress gets 0-100 while the file is sent. Errors are thrown as ApiException,
    // like the typed clients, so ApiCallHandler shows them the same way.
    Task<SongDto> UploadAsync(int playlistId, ElementReference fileInput, UploadSongRequest request,
        Action<int>? onProgress = null);
}

// Uses wwwroot/js/songUpload.js (XMLHttpRequest) for real upload progress; see the comment there.
public sealed class SongUploader(
    IJSRuntime js,
    IOptions<ServiceUrlOptions> serviceUrls,
    IAccessTokenProvider tokens) : ISongUploader, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<SongDto> UploadAsync(int playlistId, ElementReference fileInput, UploadSongRequest request,
        Action<int>? onProgress = null)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/songUpload.js");

        var url = new Uri(serviceUrls.Value.MusicService, $"playlists/{playlistId}/songs").ToString();
        var fields = new Dictionary<string, object>
        {
            ["title"] = request.Title,
            ["artistIds"] = request.ArtistIds.ToArray(),
            ["year"] = request.Year
        };
        using var progress = DotNetObjectReference.Create(new ProgressReporter(onProgress));

        var result = await _module.InvokeAsync<UploadResult>("upload", url, tokens.GetAccessToken(), fileInput, fields, progress);

        if (result.Status == -1)
            throw new InvalidOperationException("No file is selected.");
        if (result.Status == 0)
            throw new HttpRequestException("The upload could not reach the server.");

        using var response = new HttpResponseMessage((HttpStatusCode)result.Status)
        {
            Content = new StringContent(result.Body, Encoding.UTF8)
        };
        if (MediaTypeHeaderValue.TryParse(result.ContentType, out var contentType))
            response.Content.Headers.ContentType = contentType;

        return await ApiResponse.ReadAsync<SongDto>(response);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    public sealed record UploadResult(int Status, string ContentType, string Body);

    public sealed class ProgressReporter(Action<int>? onProgress)
    {
        [JSInvokable]
        public void OnProgress(long loaded, long total)
        {
            if (total > 0)
                onProgress?.Invoke((int)Math.Min(100, loaded * 100 / total));
        }
    }
}
