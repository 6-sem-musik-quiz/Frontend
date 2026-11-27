namespace Frontend.Domain.DTOs;

// RFC 9457 ProblemDetails (application/problem+json), som backend-services returnerer ved fejl.
public class ProblemDetailsDto
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }

    // SCRUM-194: the switched-off service in a maintenance answer (503), e.g. "lobbyservice".
    public string? Service { get; set; }

    // Udfyldes ved ValidationProblemDetails (400).
    public Dictionary<string, string[]>? Errors { get; set; }
}
