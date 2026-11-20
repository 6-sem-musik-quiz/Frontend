namespace Frontend.Domain.DTOs.Lobbies;

// FORELØBIG: spejler LobbyService-opgaven. Der ligger endnu ingen godkendt kontrakt i Platform/docs/contracts.
public record CreateLobbyRequest(string Name);

public record JoinLobbyRequest(string Code);

public record LobbyPlayerDto(Guid UserId, string UserName, bool IsHost);

public record LobbyDto(
    Guid Id,
    string Code,
    string Name,
    Guid HostUserId,
    string Status,
    IReadOnlyList<LobbyPlayerDto> Players,
    DateTime CreatedAt);
