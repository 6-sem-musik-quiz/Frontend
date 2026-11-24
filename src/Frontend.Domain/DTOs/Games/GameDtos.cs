namespace Frontend.Domain.DTOs.Games;

// Messages on GameService's SignalR hub /hubs/game (SCRUM-102, 103, 104), the same shape as
// GameService.Domain/Dtos/GameHubDtos.cs. SignalR sends them as camelCase JSON.
// A game is created by LobbyService (POST /api/games); everything after that goes over the hub.

// Returned by JoinGame, also after a reconnect.
public record GameStateDto(
    Guid GameId,
    GameSettingsDto Settings,
    int Round,                              // 0 before the first round has started
    int TotalRounds,
    IReadOnlyList<PlayerSummaryDto> Players,
    IReadOnlyList<CardDto> MyTimeline,      // sorted by year
    IReadOnlyList<PrefetchDto> Prefetch,    // the files to have ready now
    RoundStartedDto? CurrentRound);         // set while a round is being played

public record GameSettingsDto(int CategoryId, string CategoryName, int Rounds, int AnswerTimeSeconds);

public record PlayerSummaryDto(int UserId, string UserName, int Score);

public record CardDto(string Title, IReadOnlyList<string> Artists, int Year);

public record PrefetchDto(int Round, string AudioUrl);

// startsInMs is relative: start a local timer when the message arrives and play when it runs out.
// In GameStateDto.CurrentRound (after a reconnect) the values are what is left of the round.
public record RoundStartedDto(int Round, int TotalRounds, int StartsInMs, int StartOffsetSeconds, int AnswerTimeSeconds);

// Someone answered; the answer stays hidden until RoundEnded.
public record PlayerAnsweredDto(int UserId);

public record RoundEndedDto(int Round, CardDto Song, IReadOnlyList<RoundResultDto> Results);

// SlotIndex is null when the player did not answer.
public record RoundResultDto(int UserId, string UserName, int? SlotIndex, bool Correct, int Score, IReadOnlyList<CardDto> Timeline);

// After the last RoundEnded. Standings are sorted by score; equal scores share a placement (1, 1, 3).
public record GameEndedDto(Guid GameId, IReadOnlyList<StandingDto> Standings);

public record StandingDto(int UserId, string UserName, int Score, int Correct, int Answered, int Placement,
    IReadOnlyList<CardDto> Timeline);
