namespace Frontend.Services.Options;

// Base URLs of the backend services, bound from the "Services" section.
// In the browser they all point at edgeservice on the same host, e.g.
// https://quiz.munkcloud.dk/api/userservice/ (set in Frontend.Web/Program.cs).
// Each URL must end with "/" so the clients' relative routes are appended to it.
public class ServiceUrlOptions
{
    public const string SectionName = "Services";

    public required Uri UserService { get; init; }
    public required Uri MusicService { get; init; }
    public required Uri StatisticService { get; init; }
    public required Uri LobbyService { get; init; }
    public required Uri GameService { get; init; }
}
