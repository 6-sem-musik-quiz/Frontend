using Frontend.Domain.DTOs.Games;
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

public interface IGameApiClient
{
    Task<GameDto> StartGameAsync(StartGameRequest request, CancellationToken ct = default);
    Task<GameDto> GetGameAsync(Guid id, CancellationToken ct = default);
    Task<RoundDto> GetCurrentRoundAsync(Guid gameId, CancellationToken ct = default);
    Task<AnswerResultDto> SubmitAnswerAsync(Guid gameId, SubmitAnswerRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ScoreDto>> GetScoresAsync(Guid gameId, CancellationToken ct = default);
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

public interface IStatisticApiClient
{
    Task<PlayerStatisticDto> GetPlayerStatisticAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaderboardEntryDto>> GetLeaderboardAsync(CancellationToken ct = default);
}
