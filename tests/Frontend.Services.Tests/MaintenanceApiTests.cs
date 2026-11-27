using System.Net;

using Frontend.Services.ApiClients;
using Frontend.Services.Errors;

namespace Frontend.Services.Tests;

// SCRUM-176/194: a maintenance 503 from the edge becomes ServiceInMaintenanceException; the flags API.
public class MaintenanceApiTests
{
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://edge.test/api/lobbyservice/") };

    [Fact]
    public async Task MaintenanceProblem_IsServiceInMaintenance_WithTheServiceAndMessage()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.ServiceUnavailable,
            """{"type":"https://quiz.munkcloud.dk/problems/maintenance","title":"Lobby is under maintenance","status":503,"detail":"Back at 20:00","service":"lobbyservice"}""");

        var ex = await Assert.ThrowsAsync<ServiceInMaintenanceException>(() => new LobbyApiClient(Http(stub)).GetLobbiesAsync());

        Assert.Equal("lobbyservice", ex.Service);
        Assert.Equal("Back at 20:00", ex.Message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task AnyOther503_IsAPlainApiException()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.ServiceUnavailable,
            """{"type":"about:blank","title":"Unavailable","status":503}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => new LobbyApiClient(Http(stub)).GetLobbiesAsync());

        Assert.IsNotType<ServiceInMaintenanceException>(ex);
    }

    [Fact]
    public async Task Flags_AreReadFromTheEdge_AndASwitchIsAPut()
    {
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"service":"lobbyservice","name":"Lobby","off":true,"message":"Back at 20:00","by":"asger","since":"2026-11-27T15:00:00+00:00"}]""",
                System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new MaintenanceApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://edge.test/api/edge/") });

        var flags = await client.GetAllAsync();

        var lobby = Assert.Single(flags);
        Assert.True(lobby.Off);
        Assert.Equal("Back at 20:00", lobby.Message);
        Assert.Equal("http://edge.test/api/edge/maintenance", stub.LastRequest!.RequestUri!.ToString());
    }
}
