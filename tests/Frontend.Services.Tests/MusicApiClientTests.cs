using System.Net;
using System.Net.Http.Json;

using Frontend.Domain.DTOs.Music;
using Frontend.Services.ApiClients;
using Frontend.Services.Errors;

namespace Frontend.Services.Tests;

// Routes, methods and bodies against MusicService's contract (SCRUM-92, 94, 123),
// relative to the edge base URL /api/musicservice/.
public class MusicApiClientTests
{
    private static (MusicApiClient Client, StubHttpMessageHandler Stub) Create(HttpStatusCode status, object? body = null)
    {
        var stub = new StubHttpMessageHandler(status, body);
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/musicservice/") };
        return (new MusicApiClient(http), stub);
    }

    [Fact]
    public async Task GetPlaylists_GetsPlaylists()
    {
        var (client, stub) = Create(HttpStatusCode.OK, new[] { new PlaylistDto(1, "80s", 12) });

        var playlists = await client.GetPlaylistsAsync();

        Assert.Equal(HttpMethod.Get, stub.LastRequest!.Method);
        Assert.Equal("http://edge.test/api/musicservice/playlists", stub.LastRequest.RequestUri!.ToString());
        Assert.Equal([new PlaylistDto(1, "80s", 12)], playlists);
    }

    [Fact]
    public async Task CreatePlaylist_PostsName()
    {
        var (client, stub) = Create(HttpStatusCode.Created, new PlaylistDto(5, "Rock", 0));

        var created = await client.CreatePlaylistAsync("Rock");

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal("/api/musicservice/playlists", stub.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(new PlaylistNameRequest("Rock"), await stub.LastRequest.Content!.ReadFromJsonAsync<PlaylistNameRequest>());
        Assert.Equal(5, created.Id);
    }

    [Fact]
    public async Task RenamePlaylist_PutsName()
    {
        var (client, stub) = Create(HttpStatusCode.OK, new PlaylistDto(5, "Classic Rock", 3));

        await client.RenamePlaylistAsync(5, "Classic Rock");

        Assert.Equal(HttpMethod.Put, stub.LastRequest!.Method);
        Assert.Equal("/api/musicservice/playlists/5", stub.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(new PlaylistNameRequest("Classic Rock"), await stub.LastRequest.Content!.ReadFromJsonAsync<PlaylistNameRequest>());
    }

    [Fact]
    public async Task DeletePlaylist_Deletes()
    {
        var (client, stub) = Create(HttpStatusCode.NoContent);

        await client.DeletePlaylistAsync(5);

        Assert.Equal(HttpMethod.Delete, stub.LastRequest!.Method);
        Assert.Equal("/api/musicservice/playlists/5", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetSongs_UpdateSong_RemoveSong_UseTheSongRoutes()
    {
        var song = new SongDto(7, "Take On Me", ["a-ha"], 1985, 225, "https://media.munkcloud.dk/songs/x.mp3");
        var (client, stub) = Create(HttpStatusCode.OK, new[] { song });
        await client.GetSongsAsync(3);
        Assert.Equal("/api/musicservice/playlists/3/songs", stub.LastRequest!.RequestUri!.AbsolutePath);

        (client, stub) = Create(HttpStatusCode.OK, song);
        await client.UpdateSongAsync(7, new UpdateSongRequest("Take On Me", [4], 1985));
        Assert.Equal(HttpMethod.Put, stub.LastRequest!.Method);
        Assert.Equal("/api/musicservice/songs/7", stub.LastRequest.RequestUri!.AbsolutePath);
        var body = await stub.LastRequest.Content!.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(["title", "artistIds", "year"], body!.Keys);

        (client, stub) = Create(HttpStatusCode.NoContent);
        await client.RemoveSongAsync(3, 7);
        Assert.Equal(HttpMethod.Delete, stub.LastRequest!.Method);
        Assert.Equal("/api/musicservice/playlists/3/songs/7", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(null, "http://edge.test/api/musicservice/artists")]
    [InlineData("  ", "http://edge.test/api/musicservice/artists")]
    [InlineData(" AC/DC & co ", "http://edge.test/api/musicservice/artists?search=AC%2FDC%20%26%20co")]
    public async Task SearchArtists_EscapesTheSearchText(string? search, string expectedUrl)
    {
        var (client, stub) = Create(HttpStatusCode.OK, Array.Empty<ArtistDto>());

        await client.SearchArtistsAsync(search);

        Assert.Equal(expectedUrl, stub.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateArtist_PostsName()
    {
        var (client, stub) = Create(HttpStatusCode.Created, new ArtistDto(4, "Queen"));

        var artist = await client.CreateArtistAsync("Queen");

        Assert.Equal("/api/musicservice/artists", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(new ArtistNameRequest("Queen"), await stub.LastRequest.Content!.ReadFromJsonAsync<ArtistNameRequest>());
        Assert.Equal(new ArtistDto(4, "Queen"), artist);
    }

    [Fact]
    public async Task Conflict_IsApiExceptionWithStatus409()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.Conflict,
            """{"title":"Conflict.","status":409,"detail":"A playlist named \"80s\" already exists."}""");
        var client = new MusicApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/musicservice/") });

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.CreatePlaylistAsync("80s"));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
    }
}
