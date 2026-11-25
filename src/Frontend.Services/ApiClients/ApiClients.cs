using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Music;
using Frontend.Domain.DTOs.Statistics;
using Frontend.Domain.DTOs.Users;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;

namespace Frontend.Services.ApiClients;

// Typed HttpClients. BaseAddress og BearerTokenHandler sættes i ServiceLayerExtensions.
// Routes er relative til servicens base-URL gennem edgeservice (/api/<service>/), som
// sender /api/userservice/users videre til userservice som /api/users.
// Routes er FORELØBIGE, indtil kontrakterne er godkendt i Platform/docs/contracts.

public class UserApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IUserApiClient
{
    private const string Route = "users";

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        PostAsync<LoginRequest, LoginResponse>($"{Route}/login", request, ct);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default) =>
        PostAsync<CreateUserRequest, UserDto>(Route, request, ct);

    public Task<UserDto> GetCurrentUserAsync(CancellationToken ct = default) =>
        GetAsync<UserDto>($"{Route}/me", ct);

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default) =>
        await GetAsync<List<UserDto>>(Route, ct);
}

public class LobbyApiClient(HttpClient httpClient) : ApiClientBase(httpClient), ILobbyApiClient
{
    private const string Route = "lobbies";

    public async Task<IReadOnlyList<LobbySummaryDto>> GetLobbiesAsync(CancellationToken ct = default) =>
        await GetAsync<List<LobbySummaryDto>>(Route, ct);

    public Task<LobbyDto> GetLobbyAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<LobbyDto>($"{Route}/{id}", ct);

    public async Task<LobbyDto?> GetMyLobbyAsync(CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<LobbyDto>($"{Route}/mine", ct);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<LobbyDto> CreateLobbyAsync(CreateLobbyRequest request, CancellationToken ct = default) =>
        PostAsync<CreateLobbyRequest, LobbyDto>(Route, request, ct);

    public Task<LobbyDto> JoinLobbyAsync(Guid id, CancellationToken ct = default) =>
        PostAsync<LobbyDto>($"{Route}/{id}/join", ct);

    public Task<LobbyDto> JoinByCodeAsync(string code, CancellationToken ct = default) =>
        PostAsync<JoinByCodeRequest, LobbyDto>($"{Route}/join-by-code", new JoinByCodeRequest(code), ct);

    public Task LeaveLobbyAsync(Guid id, CancellationToken ct = default) =>
        PostWithoutBodyAsync($"{Route}/{id}/leave", ct);

    public Task<LobbyDto> SetReadyAsync(Guid id, bool ready, CancellationToken ct = default) =>
        PostAsync<ReadyRequest, LobbyDto>($"{Route}/{id}/ready", new ReadyRequest(ready), ct);

    public Task<StartGameResponse> StartGameAsync(Guid id, CancellationToken ct = default) =>
        PostAsync<StartGameResponse>($"{Route}/{id}/start", ct);
}

public class MusicApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IMusicApiClient
{
    public async Task<IReadOnlyList<PlaylistDto>> GetPlaylistsAsync(CancellationToken ct = default) =>
        await GetAsync<List<PlaylistDto>>("playlists", ct);

    public Task<PlaylistDto> GetPlaylistAsync(int id, CancellationToken ct = default) =>
        GetAsync<PlaylistDto>($"playlists/{id}", ct);

    public Task<PlaylistDto> CreatePlaylistAsync(string name, CancellationToken ct = default) =>
        PostAsync<PlaylistNameRequest, PlaylistDto>("playlists", new PlaylistNameRequest(name), ct);

    public Task<PlaylistDto> RenamePlaylistAsync(int id, string name, CancellationToken ct = default) =>
        PutAsync<PlaylistNameRequest, PlaylistDto>($"playlists/{id}", new PlaylistNameRequest(name), ct);

    public Task DeletePlaylistAsync(int id, CancellationToken ct = default) =>
        DeleteAsync($"playlists/{id}", ct);

    public async Task<IReadOnlyList<SongDto>> GetSongsAsync(int playlistId, CancellationToken ct = default) =>
        await GetAsync<List<SongDto>>($"playlists/{playlistId}/songs", ct);

    public Task<SongDto> UpdateSongAsync(int songId, UpdateSongRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateSongRequest, SongDto>($"songs/{songId}", request, ct);

    public Task RemoveSongAsync(int playlistId, int songId, CancellationToken ct = default) =>
        DeleteAsync($"playlists/{playlistId}/songs/{songId}", ct);

    public async Task<IReadOnlyList<ArtistDto>> SearchArtistsAsync(string? search, CancellationToken ct = default) =>
        await GetAsync<List<ArtistDto>>(
            string.IsNullOrWhiteSpace(search) ? "artists" : $"artists?search={Uri.EscapeDataString(search.Trim())}", ct);

    public Task<ArtistDto> CreateArtistAsync(string name, CancellationToken ct = default) =>
        PostAsync<ArtistNameRequest, ArtistDto>("artists", new ArtistNameRequest(name), ct);
}

public class StatisticApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IStatisticApiClient
{
    private const string Route = "statistics";

    public Task<WeeklyLeaderboardDto> GetWeeklyLeaderboardAsync(string week = "current", CancellationToken ct = default) =>
        GetAsync<WeeklyLeaderboardDto>($"{Route}/weekly?week={Uri.EscapeDataString(week)}", ct);

    public Task<MyStatsDto> GetMyStatsAsync(CancellationToken ct = default) =>
        GetAsync<MyStatsDto>($"{Route}/me", ct);
}
