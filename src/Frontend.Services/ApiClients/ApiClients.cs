using Frontend.Domain.DTOs.Games;
using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Music;
using Frontend.Domain.DTOs.Statistics;
using Frontend.Domain.DTOs.Users;
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

    public async Task<IReadOnlyList<LobbyDto>> GetLobbiesAsync(CancellationToken ct = default) =>
        await GetAsync<List<LobbyDto>>(Route, ct);

    public Task<LobbyDto> GetLobbyAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<LobbyDto>($"{Route}/{id}", ct);

    public Task<LobbyDto> CreateLobbyAsync(CreateLobbyRequest request, CancellationToken ct = default) =>
        PostAsync<CreateLobbyRequest, LobbyDto>(Route, request, ct);

    public Task<LobbyDto> JoinLobbyAsync(JoinLobbyRequest request, CancellationToken ct = default) =>
        PostAsync<JoinLobbyRequest, LobbyDto>($"{Route}/join", request, ct);

    public Task LeaveLobbyAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Route}/{id}/players/me", ct);
}

public class GameApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IGameApiClient
{
    private const string Route = "games";

    public Task<GameDto> StartGameAsync(StartGameRequest request, CancellationToken ct = default) =>
        PostAsync<StartGameRequest, GameDto>(Route, request, ct);

    public Task<GameDto> GetGameAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GameDto>($"{Route}/{id}", ct);

    public Task<RoundDto> GetCurrentRoundAsync(Guid gameId, CancellationToken ct = default) =>
        GetAsync<RoundDto>($"{Route}/{gameId}/rounds/current", ct);

    public Task<AnswerResultDto> SubmitAnswerAsync(Guid gameId, SubmitAnswerRequest request, CancellationToken ct = default) =>
        PostAsync<SubmitAnswerRequest, AnswerResultDto>($"{Route}/{gameId}/answers", request, ct);

    public async Task<IReadOnlyList<ScoreDto>> GetScoresAsync(Guid gameId, CancellationToken ct = default) =>
        await GetAsync<List<ScoreDto>>($"{Route}/{gameId}/scores", ct);
}

public class MusicApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IMusicApiClient
{
    private const string Route = "tracks";

    public async Task<IReadOnlyList<TrackDto>> GetTracksAsync(CancellationToken ct = default) =>
        await GetAsync<List<TrackDto>>(Route, ct);

    public Task<TrackDto> GetRandomTrackAsync(CancellationToken ct = default) =>
        GetAsync<TrackDto>($"{Route}/random", ct);

    public Task<TrackDto> CreateTrackAsync(CreateTrackRequest request, CancellationToken ct = default) =>
        PostAsync<CreateTrackRequest, TrackDto>(Route, request, ct);

    public Task DeleteTrackAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Route}/{id}", ct);
}

public class StatisticApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IStatisticApiClient
{
    private const string Route = "statistics";

    public Task<PlayerStatisticDto> GetPlayerStatisticAsync(Guid userId, CancellationToken ct = default) =>
        GetAsync<PlayerStatisticDto>($"{Route}/players/{userId}", ct);

    public async Task<IReadOnlyList<LeaderboardEntryDto>> GetLeaderboardAsync(CancellationToken ct = default) =>
        await GetAsync<List<LeaderboardEntryDto>>($"{Route}/leaderboard", ct);
}
