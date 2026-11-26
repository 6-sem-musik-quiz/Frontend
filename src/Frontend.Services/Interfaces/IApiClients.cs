using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Music;
using Frontend.Domain.DTOs.Statistics;
using Frontend.Domain.DTOs.Users;

namespace Frontend.Services.Interfaces;

// Siderne bruger kun disse interfaces, aldrig HttpClient direkte.
// Alle metoder kaster ApiException (UnauthorizedApiException ved 401) ved fejlsvar.

public interface IUserApiClient
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> GetCurrentUserAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default);
}

// LobbyService. Live updates come over SignalR (ILobbyHub in Frontend.Web), not through this client.
public interface ILobbyApiClient
{
    Task<IReadOnlyList<LobbySummaryDto>> GetLobbiesAsync(CancellationToken ct = default);
    Task<LobbyDto> GetLobbyAsync(Guid id, CancellationToken ct = default);
    // The waiting lobby the user is in, or null (LobbyService answers 404).
    Task<LobbyDto?> GetMyLobbyAsync(CancellationToken ct = default);
    Task<LobbyDto> CreateLobbyAsync(CreateLobbyRequest request, CancellationToken ct = default);
    Task<LobbyDto> JoinLobbyAsync(Guid id, CancellationToken ct = default);
    Task<LobbyDto> JoinByCodeAsync(string code, CancellationToken ct = default);
    Task LeaveLobbyAsync(Guid id, CancellationToken ct = default);
    Task<LobbyDto> SetReadyAsync(Guid id, bool ready, CancellationToken ct = default);
    Task<StartGameResponse> StartGameAsync(Guid id, CancellationToken ct = default);
}

// MusicService. Writes are admin only (403 for players). Upload goes through ISongUploader in
// Frontend.Web instead, because it needs the browser's file input and upload progress.
public interface IMusicApiClient
{
    Task<IReadOnlyList<PlaylistDto>> GetPlaylistsAsync(CancellationToken ct = default);
    Task<PlaylistDto> GetPlaylistAsync(int id, CancellationToken ct = default);
    Task<PlaylistDto> CreatePlaylistAsync(string name, CancellationToken ct = default);
    Task<PlaylistDto> RenamePlaylistAsync(int id, string name, CancellationToken ct = default);
    Task DeletePlaylistAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<SongDto>> GetSongsAsync(int playlistId, CancellationToken ct = default);
    Task<SongDto> UpdateSongAsync(int songId, UpdateSongRequest request, CancellationToken ct = default);
    Task RemoveSongAsync(int playlistId, int songId, CancellationToken ct = default);

    Task<IReadOnlyList<ArtistDto>> SearchArtistsAsync(string? search, CancellationToken ct = default);
    Task<ArtistDto> CreateArtistAsync(string name, CancellationToken ct = default);
}

// StatisticService. Both endpoints need a login.
public interface IStatisticApiClient
{
    // week: "current" or an ISO week such as "2026-W48".
    Task<WeeklyLeaderboardDto> GetWeeklyLeaderboardAsync(string week = "current", CancellationToken ct = default);
    Task<MyStatsDto> GetMyStatsAsync(CancellationToken ct = default);
}
