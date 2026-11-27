using System.Net;

using Frontend.Domain.DTOs;

namespace Frontend.Services.Errors;

// Kastes af API-klienterne, når en service svarer med en fejlstatus.
// Problem er udfyldt, hvis svaret var application/problem+json.
public class ApiException(HttpStatusCode statusCode, string message, ProblemDetailsDto? problem = null)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public ProblemDetailsDto? Problem { get; } = problem;
}

// 401 fra en service: token mangler eller er udløbet. UI'et sender brugeren til /login.
public class UnauthorizedApiException(string message, ProblemDetailsDto? problem = null)
    : ApiException(HttpStatusCode.Unauthorized, message, problem);

// SCRUM-176/194: 503 from a service an admin has switched off ("design to be disabled"). The edge
// (and GameService, via LobbyService) answers with ProblemDetails of type ProblemType; Service is the
// switched-off service and Detail the admin's message, if any. The UI shows a maintenance toast.
public class ServiceInMaintenanceException(string service, string message, ProblemDetailsDto? problem = null)
    : ApiException(HttpStatusCode.ServiceUnavailable, message, problem)
{
    public const string ProblemType = "https://quiz.munkcloud.dk/problems/maintenance";

    public string Service { get; } = service;
}
