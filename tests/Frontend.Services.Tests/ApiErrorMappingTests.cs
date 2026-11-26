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
            new AuthLoginResponse("token-123", DateTime.UtcNow.AddMinutes(15), "refresh-1", DateTime.UtcNow.AddDays(7),
                new UserDto(1, "bob", Roles.Player)));
        var client = new UserApiClient(Http(stub));

        var result = await client.LoginAsync(new LoginRequest { UserName = "bob", Password = "pw" });

        Assert.Equal("token-123", result.AccessToken);
        Assert.Equal("refresh-1", result.RefreshToken);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(new Uri(BaseAddress, "auth/login"), stub.LastRequest.RequestUri);
    }

    // The exact JSON UserService returns from POST /api/auth/login (camelCase, int id, role as text).
    [Fact]
    public async Task Login_ReadsUserServiceResponse()
    {
        const string json = """
            {"accessToken":"aaa.bbb.ccc","accessTokenExpiresAt":"2026-11-24T22:00:00Z","refreshToken":"r-1","refreshTokenExpiresAt":"2026-12-01T21:45:00Z","user":{"id":7,"userName":"alice","role":"Player"}}
            """;
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new UserApiClient(Http(stub));

        var result = await client.LoginAsync(new LoginRequest { UserName = "alice", Password = "password1" });

        Assert.Equal("aaa.bbb.ccc", result.AccessToken);
        Assert.Equal(DateTime.Parse("2026-11-24T22:00:00Z").ToUniversalTime(), result.AccessTokenExpiresAt.ToUniversalTime());
        Assert.Equal("r-1", result.RefreshToken);
        Assert.Equal(new UserDto(7, "alice", Roles.Player), result.User);
    }

    [Fact]
    public async Task Refresh_PostsRefreshToken_AndReadsNewPair()
    {
        const string json = """
            {"accessToken":"new.access.token","accessTokenExpiresAt":"2026-11-24T22:15:00Z","refreshToken":"r-2","refreshTokenExpiresAt":"2026-12-01T22:00:00Z"}
            """;
        string? sentBody = null;
        var stub = new StubHttpMessageHandler(request =>
        {
            sentBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var client = new UserApiClient(Http(stub));

        var result = await client.RefreshAsync("r-1");

        Assert.Equal(new Uri(BaseAddress, "auth/refresh"), stub.LastRequest!.RequestUri);
        Assert.Equal("""{"refreshToken":"r-1"}""", sentBody);
        Assert.Equal("new.access.token", result.AccessToken);
        Assert.Equal("r-2", result.RefreshToken);
    }

    [Fact]
    public async Task Refresh_Rejected_ThrowsUnauthorized()
    {
        var stub = StubHttpMessageHandler.ProblemDetails(HttpStatusCode.Unauthorized,
            """{"title":"Unauthorized","status":401,"detail":"Invalid or expired refresh token."}""");
        var client = new UserApiClient(Http(stub));

        await Assert.ThrowsAsync<UnauthorizedApiException>(() => client.RefreshAsync("old"));
    }

    [Fact]
    public async Task Logout_PostsRefreshToken()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.NoContent);
        var client = new UserApiClient(Http(stub));

        await client.LogoutAsync("r-1");

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(new Uri(BaseAddress, "auth/logout"), stub.LastRequest.RequestUri);
    }
}
