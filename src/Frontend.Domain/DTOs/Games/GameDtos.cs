namespace Frontend.Domain.DTOs.Games;

// FORELØBIG: spejler GameService-opgaven. Der ligger endnu ingen godkendt kontrakt i Platform/docs/contracts.
public record StartGameRequest(Guid LobbyId, int Rounds);

public record GameDto(Guid Id, Guid LobbyId, string Status, int CurrentRound, int TotalRounds);

public record RoundDto(Guid Id, int Number, string? PreviewUrl, IReadOnlyList<string> Options, DateTime EndsAt);

public record SubmitAnswerRequest(Guid RoundId, string Answer);

public record AnswerResultDto(bool IsCorrect, int Points, string CorrectAnswer);

public record ScoreDto(Guid UserId, string UserName, int Score);
