using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Frontend.Web.Tests;

public class HealthCheckTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Beskyttede sider håndhæves i Blazor (AuthorizeRouteView), så server-endpointet skal
    // levere app-skallen i stedet for at challenge uden auth-scheme (som ville give 500).
    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/login")]
    public async Task Pages_ReturnOk(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
