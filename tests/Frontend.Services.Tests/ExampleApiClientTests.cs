using System.Net;

using Frontend.Domain.DTOs;
using Frontend.Services.ApiClients;

namespace Frontend.Services.Tests;

public class ExampleApiClientTests
{
    private static readonly Uri BaseAddress = new("http://userservice.test/");

    private static ExampleApiClient CreateSut(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = BaseAddress });

    [Fact]
    public async Task GetAllAsync_ReturnsExamplesFromService()
    {
        var expected = new[] { new ExampleResponse(Guid.NewGuid(), "Test", "Desc", DateTime.UtcNow) };
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, expected);

        var result = await CreateSut(handler).GetAllAsync();

        Assert.Single(result);
        Assert.Equal("Test", result[0].Name);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(new Uri(BaseAddress, "api/example"), handler.LastRequest.RequestUri);
    }

    [Fact]
    public async Task CreateAsync_PostsRequestAndReturnsCreatedExample()
    {
        var created = new ExampleResponse(Guid.NewGuid(), "New", null, DateTime.UtcNow);
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, created);

        var result = await CreateSut(handler).CreateAsync(new ExampleRequest { Name = "New" });

        Assert.Equal(created.Id, result.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenServiceReturnsError()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateSut(handler).CreateAsync(new ExampleRequest { Name = "Bad" }));
    }
}
