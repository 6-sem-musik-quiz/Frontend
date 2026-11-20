namespace Frontend.Domain.DTOs.Statistics;

// FORELØBIG: StatisticService har endnu ingen godkendt kontrakt i Platform/docs/contracts.
public record PlayerStatisticDto(Guid UserId, string UserName, int GamesPlayed, int Wins, int TotalScore);

public record LeaderboardEntryDto(int Rank, Guid UserId, string UserName, int TotalScore);
