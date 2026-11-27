using Frontend.Domain.Localization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Frontend.Domain.DTOs;
using Frontend.Services.Errors;

namespace Frontend.Services.Http;

// Fælles mapping af HTTP-svar til resultat eller ApiException.
public static class ApiResponse
{
    public const string ProblemJsonMediaType = "application/problem+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await TryReadProblemAsync(response, ct);
        var message = BuildMessage(problem, response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedApiException(message, problem);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable
            && problem?.Type == ServiceInMaintenanceException.ProblemType)
        {
            throw new ServiceInMaintenanceException(problem.Service ?? "", problem.Detail ?? message, problem);
        }

        throw new ApiException(response.StatusCode, message, problem);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct = default)
    {
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new ApiException(response.StatusCode, "Servicen returnerede et tomt svar.");
    }

    private static async Task<ProblemDetailsDto?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, ProblemJsonMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildMessage(ProblemDetailsDto? problem, HttpStatusCode statusCode)
    {
        if (problem is null)
        {
            return Lang.T($"Servicen svarede med fejl {(int)statusCode}.", $"The service answered with error {(int)statusCode}.");
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(problem.Title))
        {
            parts.Add(problem.Title.Trim());
        }
        if (!string.IsNullOrWhiteSpace(problem.Detail))
        {
            parts.Add(problem.Detail.Trim());
        }
        if (problem.Errors is { Count: > 0 })
        {
            parts.AddRange(problem.Errors.SelectMany(e => e.Value));
        }

        return parts.Count > 0 ? string.Join(" ", parts) : Lang.T($"Servicen svarede med fejl {(int)statusCode}.", $"The service answered with error {(int)statusCode}.");
    }
}
