using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Services.Interfaces;
using Frontend.Web.Components.Pages;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Frontend.Web.Tests;

// Component tests with bUnit: render a page with a substituted API client, no backend needed.
public class ExamplesPageTests : BunitContext
{
    private readonly IExampleApiClient _apiClient = Substitute.For<IExampleApiClient>();

    public ExamplesPageTests()
    {
        Services.AddSingleton(_apiClient);
    }

    [Fact]
    public void ShowsExamplesFromApi()
    {
        _apiClient.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new ExampleResponse(Guid.NewGuid(), "Bohemian Rhapsody", "Queen", DateTime.UtcNow)]);

        var page = Render<Examples>();

        Assert.Contains("Bohemian Rhapsody", page.Find("table").TextContent);
    }

    [Fact]
    public void ShowsWarning_WhenServiceIsUnreachable()
    {
        _apiClient.GetAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());

        var page = Render<Examples>();

        Assert.Contains("Could not reach UserService", page.Find(".alert").TextContent);
    }
}
