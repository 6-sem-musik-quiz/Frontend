using System.Net.Http.Json;

using Frontend.Services.Http;

namespace Frontend.Services.ApiClients;

// Fælles hjælpemetoder for de typed HttpClients. Alle fejl mappes via ApiResponse til ApiException.
public abstract class ApiClientBase(HttpClient httpClient)
{
    protected async Task<T> GetAsync<T>(string route, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(route, ct);
        return await ApiResponse.ReadAsync<T>(response, ct);
    }

    protected async Task<TResponse> PostAsync<TRequest, TResponse>(string route, TRequest body, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(route, body, ct);
        return await ApiResponse.ReadAsync<TResponse>(response, ct);
    }

    protected async Task PostAsync<TRequest>(string route, TRequest body, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(route, body, ct);
        await ApiResponse.EnsureSuccessAsync(response, ct);
    }

    protected async Task<TResponse> PostAsync<TResponse>(string route, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(route, content: null, ct);
        return await ApiResponse.ReadAsync<TResponse>(response, ct);
    }

    protected async Task<TResponse> PutAsync<TRequest, TResponse>(string route, TRequest body, CancellationToken ct)
    {
        using var response = await httpClient.PutAsJsonAsync(route, body, ct);
        return await ApiResponse.ReadAsync<TResponse>(response, ct);
    }

    protected async Task DeleteAsync(string route, CancellationToken ct)
    {
        using var response = await httpClient.DeleteAsync(route, ct);
        await ApiResponse.EnsureSuccessAsync(response, ct);
    }
}
