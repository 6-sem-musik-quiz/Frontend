using System.Net;

using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Users;
using Frontend.Services.ApiClients;
using Frontend.Services.Errors;

namespace Frontend.Services.Tests;

public class ApiErrorMappingTests
{
    private static readonly Uri BaseAddress = new("http://service.test/");

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = BaseAddress };

    [Fact]
    public async Task ProblemDetails_IsMappedToApiException_WithReadableMessage()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.Conflict,
            """{"type":"about:blank","title":"Lobbyen er fuld","status":409,"detail":"Der er allerede 8 spillere."}""");
        var client = new LobbyApiClient(Http(stub));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.JoinByCodeAsync("ABCD"));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.NotNull(ex.Problem);
        Assert.Equal("Lobbyen er fuld", ex.Problem.Title);
        Assert.Equal("Der er allerede 8 spillere.", ex.Problem.Detail);
        Assert.Equal("Lobbyen er fuld Der er allerede 8 spillere.", ex.Message);
    }

    [Fact]
    public async Task ValidationProblemDetails_IncludesFieldErrorsInMessage()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.BadRequest,
            """{"title":"Ugyldige data","status":400,"errors":{"UserName":["Brugernavnet er taget."]}}""");
        var client = new UserApiClient(Http(stub));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            client.CreateUserAsync(new CreateUserRequest { UserName = "bob", Password = "hemmelig" }));

        Assert.Contains("Brugernavnet er taget.", ex.Message);
        Assert.Equal(["Brugernavnet er taget."], ex.Problem!.Errors!["UserName"]);
    }

    [Fact]
    public async Task Unauthorized_IsMappedToUnauthorizedApiException()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.Unauthorized);
        var client = new UserApiClient(Http(stub));

        var ex = await Assert.ThrowsAsync<UnauthorizedApiException>(() => client.GetCurrentUserAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ErrorWithoutProblemDetails_GivesApiExceptionWithoutProblem()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        var client = new LobbyApiClient(Http(stub));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetLobbiesAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Null(ex.Problem);
    }

    [Fact]
    public async Task Success_ReturnsDeserializedBody()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK,
            new LoginResponse("token-123", DateTime.UtcNow.AddHours(12), new UserDto(1, "bob", Roles.Player)));
        var client = new UserApiClient(Http(stub));

        var result = await client.LoginAsync(new LoginRequest { UserName = "bob", Password = "pw" });

        Assert.Equal("token-123", result.Token);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(new Uri(BaseAddress, "users/login"), stub.LastRequest.RequestUri);
    }

    // The exact JSON UserService returns from POST /api/users/login (camelCase, int id, role as text).
    [Fact]
    public async Task Login_ReadsUserServiceResponse()
    {
        const string json = """
            {"token":"aaa.bbb.ccc","expiresAt":"2026-11-24T22:00:00Z","user":{"id":7,"userName":"alice","role":"Player"}}
            """;
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new UserApiClient(Http(stub));

        var result = await client.LoginAsync(new LoginRequest { UserName = "alice", Password = "password1" });

        Assert.Equal("aaa.bbb.ccc", result.Token);
        Assert.Equal(DateTime.Parse("2026-11-24T22:00:00Z").ToUniversalTime(), result.ExpiresAt.ToUniversalTime());
        Assert.Equal(new UserDto(7, "alice", Roles.Player), result.User);
    }
}
