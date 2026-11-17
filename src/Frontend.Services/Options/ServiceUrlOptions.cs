namespace Frontend.Services.Options;

// Base URLs of the backend services. Bound from the "Services" section in appsettings.
public class ServiceUrlOptions
{
    public const string SectionName = "Services";

    public required Uri UserService { get; init; }
    public required Uri MusicService { get; init; }
    public required Uri StatisticService { get; init; }
    public required Uri LobbyService { get; init; }
    public required Uri GameService { get; init; }
}
