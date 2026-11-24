namespace Frontend.Domain.DTOs.Lobbies;

// Mirrors LobbyService (SCRUM-96 to SCRUM-100). Through the edge: /api/lobbyservice/...

// Body of POST /lobbies. AnswerTimeSeconds 15/30/45/60, Rounds 5/10/15/20, MaxPlayers 2-8.
public record CreateLobbyRequest(int CategoryId, int AnswerTimeSeconds, int Rounds, int MaxPlayers, bool IsPrivate);

// Body of POST /lobbies/join-by-code. Matched case-insensitively.
public record JoinByCodeRequest(string Code);

// Body of POST /lobbies/{id}/ready.
public record ReadyRequest(bool Ready);

// Answer of POST /lobbies/{id}/start and payload of the hub message GameStarted.
public record StartGameResponse(Guid GameId);

public record LobbySettingsDto(int CategoryId, string CategoryName, int AnswerTimeSeconds, int Rounds, int MaxPlayers);

public record LobbyPlayerDto(int UserId, string UserName, bool IsReady);

// Code is only set for private lobbies. The host is always Players[0]. Status: "Waiting" or "InGame".
public record LobbyDto(
    Guid Id,
    string? Code,
    int HostUserId,
    bool IsPrivate,
    LobbySettingsDto Settings,
    IReadOnlyList<LobbyPlayerDto> Players,
    string Status);

// One row in GET /lobbies: public lobbies waiting for players, newest first. Full lobbies are included.
public record LobbySummaryDto(
    Guid Id,
    string HostUserName,
    string CategoryName,
    int PlayerCount,
    int MaxPlayers,
    int Rounds,
    int AnswerTimeSeconds);
