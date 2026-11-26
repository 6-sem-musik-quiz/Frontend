namespace Frontend.Domain.DTOs.Games;

// Messages on GameService's SignalR hub /hubs/game (SCRUM-102, 103, 104, 150), the same shape as
// GameService.Domain/Dtos/GameHubDtos.cs. SignalR sends them as camelCase JSON.
// A game is created by LobbyService (POST /api/games); everything after that goes over the hub.
// Fields added later (SCRUM-150) are optional here, so an older server still deserializes.

// Returned by JoinGame, also after a reconnect.
public record GameStateDto(
    Guid GameId,
    GameSettingsDto Settings,
    int Round,                              // 0 before the first round has started
    int TotalRounds,
    IReadOnlyList<PlayerSummaryDto> Players,
    IReadOnlyList<CardDto> MyTimeline,      // sorted by year
    IReadOnlyList<PrefetchDto> Prefetch,    // the files to have ready now
    RoundStartedDto? CurrentRound,          // set while a round is being played
    IReadOnlyList<PastSongDto>? PastSongs = null); // songs of the rounds that have ended

public record GameSettingsDto(int CategoryId, string CategoryName, int Rounds, int AnswerTimeSeconds);

// Timeline and History cover ended rounds only (public: everyone got the same songs).
// AnsweredOrder (1, 2, 3 ...) is set while a round runs and the player has answered it.
public record PlayerSummaryDto(
    int UserId,
    string UserName,
    int Score,
    IReadOnlyList<CardDto>? Timeline = null,
    IReadOnlyList<RoundHistoryDto>? History = null,
    int? AnsweredOrder = null);

// AnsweredAfterMs is null when the player did not answer that round.
public record RoundHistoryDto(int Round, bool Correct, int? AnsweredAfterMs);

public record PastSongDto(int Round, CardDto Song);

public record CardDto(string Title, IReadOnlyList<string> Artists, int Year);

public record PrefetchDto(int Round, string AudioUrl);

// startsInMs is relative: start a local timer when the message arrives and play when it runs out.
// SCRUM-168 (gameservice.gamehub v1.1): PlaybackStartsAt is the server time at which the song is at
// StartOffsetSeconds, and the answer time ends AnswerTimeSeconds later. With the clock offset from
// ServerTime, every player starts at that same instant; without it (or from an older server), use startsInMs.
// In GameStateDto.CurrentRound (after a reconnect) the values are what is left of the round.
public record RoundStartedDto(int Round, int TotalRounds, int StartsInMs, int StartOffsetSeconds, int AnswerTimeSeconds,
    DateTimeOffset? PlaybackStartsAt = null);

// Someone answered; the answer stays hidden until RoundEnded. AnsweredAfterMs is measured by the
// server from when the song started playing.
public record PlayerAnsweredDto(int UserId, int AnsweredAfterMs = 0);

public record RoundEndedDto(int Round, CardDto Song, IReadOnlyList<RoundResultDto> Results);

// SlotIndex, AnsweredAfterMs and Range are null when the player did not answer.
// Range = [from, to]: the years the answer was put between; null for an open end.
public record RoundResultDto(
    int UserId,
    string UserName,
    int? SlotIndex,
    bool Correct,
    int Score,
    IReadOnlyList<CardDto> Timeline,
    int? AnsweredAfterMs = null,
    IReadOnlyList<int?>? Range = null);

// After the last RoundEnded. Standings are sorted by score; equal scores share a placement (1, 1, 3).
// SCRUM-151 (gameservice.gamehub@1.2): Awards ("fastest", then "streak") and Songs (every round's song).
// The bonus is in Score and Placement already; a player's bonus is the number of awards they won.
// Null from an older server: the end screen then shows the standings at once (no staged reveal).
public record GameEndedDto(
    Guid GameId,
    IReadOnlyList<StandingDto> Standings,
    IReadOnlyList<AwardDto>? Awards = null,
    IReadOnlyList<PastSongDto>? Songs = null);

// Kind is "fastest" (Lynet) or "streak" (Stimen). Winners got +1 (empty if nobody qualified).
// Contenders are every player, best first.
public record AwardDto(string Kind, IReadOnlyList<int> Winners, IReadOnlyList<AwardContenderDto> Contenders);

// Value: "fastest" = average answer time in ms over right answers (null with none); "streak" = longest run.
public record AwardContenderDto(int UserId, int? Value, bool Qualified);

public record StandingDto(int UserId, string UserName, int Score, int Correct, int Answered, int Placement,
    IReadOnlyList<CardDto> Timeline);
