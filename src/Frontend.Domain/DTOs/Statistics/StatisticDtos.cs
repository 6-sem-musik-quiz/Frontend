namespace Frontend.Domain.DTOs.Statistics;

// StatisticService (SCRUM-106/107). The Platform contract is not merged yet; these mirror
// StatisticService.Domain.DTOs on main. WinRate and Accuracy are fractions 0-1 (0.75 = 75 %).

// GET /api/statistics/weekly?week=current. WeekStart is Monday 00:00 and WeekEnd Sunday 23:59:59,
// both with the Copenhagen offset of that moment.
public record WeeklyLeaderboardDto(
    DateTimeOffset WeekStart,
    DateTimeOffset WeekEnd,
    IReadOnlyList<MostWinsEntryDto> MostWins,
    IReadOnlyList<WinRateEntryDto> WinRate,
    IReadOnlyList<AccuracyEntryDto> Accuracy);

public record MostWinsEntryDto(int UserId, string UserName, int Wins, int GamesPlayed);

public record WinRateEntryDto(int UserId, string UserName, double WinRate, int GamesPlayed);

public record AccuracyEntryDto(int UserId, string UserName, double Accuracy, int GamesPlayed);

// GET /api/statistics/me (the user is the token's "sub").
public record MyStatsDto(
    int GamesPlayed,
    int Wins,
    double WinRate,
    double Accuracy,
    int RoundsPlayed,
    long TimePlayedSeconds,
    MostPlayedCategoryDto? MostPlayedCategory,
    MostCommonFriendDto? MostCommonFriend);

public record MostPlayedCategoryDto(int CategoryId, string CategoryName, int Games);

public record MostCommonFriendDto(int UserId, string UserName, int GamesTogether);
