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

public interface ILobbyApiClient
{
    Task<IReadOnlyList<LobbyDto>> GetLobbiesAsync(CancellationToken ct = default);
    Task<LobbyDto> GetLobbyAsync(Guid id, CancellationToken ct = default);
    Task<LobbyDto> CreateLobbyAsync(CreateLobbyRequest request, CancellationToken ct = default);
    Task<LobbyDto> JoinLobbyAsync(JoinLobbyRequest request, CancellationToken ct = default);
    Task LeaveLobbyAsync(Guid id, CancellationToken ct = default);
}

public interface IGameApiClient
{
    Task<GameDto> StartGameAsync(StartGameRequest request, CancellationToken ct = default);
    Task<GameDto> GetGameAsync(Guid id, CancellationToken ct = default);
    Task<RoundDto> GetCurrentRoundAsync(Guid gameId, CancellationToken ct = default);
    Task<AnswerResultDto> SubmitAnswerAsync(Guid gameId, SubmitAnswerRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ScoreDto>> GetScoresAsync(Guid gameId, CancellationToken ct = default);
}

public interface IMusicApiClient
{
    Task<IReadOnlyList<TrackDto>> GetTracksAsync(CancellationToken ct = default);
    Task<TrackDto> GetRandomTrackAsync(CancellationToken ct = default);
    Task<TrackDto> CreateTrackAsync(CreateTrackRequest request, CancellationToken ct = default);
    Task DeleteTrackAsync(Guid id, CancellationToken ct = default);
}

public interface IStatisticApiClient
{
    Task<PlayerStatisticDto> GetPlayerStatisticAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaderboardEntryDto>> GetLeaderboardAsync(CancellationToken ct = default);
}
