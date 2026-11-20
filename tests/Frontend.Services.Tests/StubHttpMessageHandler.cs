using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Frontend.Services.Tests;

// Returns a canned response and records the requests, so API clients can be tested without a running service.
public class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public StubHttpMessageHandler(HttpStatusCode statusCode, object? body = null)
        : this(_ => CreateResponse(statusCode, body))
    {
    }

    public List<HttpRequestMessage> Requests { get; } = [];
    public HttpRequestMessage? LastRequest => Requests.LastOrDefault();

    public static StubHttpMessageHandler ProblemDetails(HttpStatusCode statusCode, string json) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, MediaTypeHeaderValue.Parse("application/problem+json")),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, object? body)
    {
        var response = new HttpResponseMessage(statusCode);
        if (body is not null)
        {
            response.Content = JsonContent.Create(body);
        }
        return response;
    }
}
