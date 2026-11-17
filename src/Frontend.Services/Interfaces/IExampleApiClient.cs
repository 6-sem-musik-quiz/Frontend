using Frontend.Domain.DTOs;

namespace Frontend.Services.Interfaces;

public interface IExampleApiClient
{
    Task<IReadOnlyList<ExampleResponse>> GetAllAsync(CancellationToken ct = default);
    Task<ExampleResponse> CreateAsync(ExampleRequest request, CancellationToken ct = default);
}
