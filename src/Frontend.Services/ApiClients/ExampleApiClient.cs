using System.Net.Http.Json;

using Frontend.Domain.DTOs;
using Frontend.Services.Interfaces;

namespace Frontend.Services.ApiClients;

// Typed HttpClient for the example endpoints in UserService. BaseAddress is set in ServiceLayerExtensions.
public class ExampleApiClient(HttpClient httpClient) : IExampleApiClient
{
    private const string Route = "api/example";

    public async Task<IReadOnlyList<ExampleResponse>> GetAllAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<ExampleResponse>>(Route, ct) ?? [];

    public async Task<ExampleResponse> CreateAsync(ExampleRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(Route, request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ExampleResponse>(ct)
            ?? throw new InvalidOperationException("UserService returned an empty response.");
    }
}
