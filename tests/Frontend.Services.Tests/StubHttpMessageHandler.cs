using System.Net;
using System.Net.Http.Json;

namespace Frontend.Services.Tests;

// Returns a canned response and records the last request, so API clients can be tested without a running service.
public class StubHttpMessageHandler(HttpStatusCode statusCode, object? body = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        var response = new HttpResponseMessage(statusCode);
        if (body is not null)
        {
            response.Content = JsonContent.Create(body);
        }

        return Task.FromResult(response);
    }
}
