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
