using System.Net;

using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Users;
using Frontend.Services.ApiClients;
using Frontend.Services.Auth;
using Frontend.Services.Extensions;
using Frontend.Services.Http;
using Frontend.Services.Interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace Frontend.Services.Tests;

public class BearerTokenHandlerTests
{
    private static readonly Uri BaseAddress = new("http://userservice.test/");

    private static (UserApiClient Client, StubHttpMessageHandler Stub) CreateClient(string? token)
    {
        var tokenProvider = Substitute.For<IAccessTokenProvider>();
        tokenProvider.GetAccessToken().Returns(token);

        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Array.Empty<UserDto>());
        var handler = new BearerTokenHandler(tokenProvider) { InnerHandler = stub };
        return (new UserApiClient(new HttpClient(handler) { BaseAddress = BaseAddress }), stub);
    }

    [Fact]
    public async Task SetsBearerToken_OnRequest()
    {
        var (client, stub) = CreateClient("abc.def.ghi");

        await client.GetUsersAsync();

        var auth = stub.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("abc.def.ghi", auth.Parameter);
    }

    [Fact]
    public async Task OmitsAuthorizationHeader_WhenNoToken()
    {
        var (client, stub) = CreateClient(null);

        await client.GetUsersAsync();

        Assert.Null(stub.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task AddServiceLayer_SetsTokenAndBaseAddress_OnAllTypedClients()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, Array.Empty<LobbyDto>());
        var tokenProvider = Substitute.For<IAccessTokenProvider>();
        tokenProvider.GetAccessToken().Returns("user-token");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:UserService"] = "http://edge.test/api/userservice/",
                ["Services:LobbyService"] = "http://edge.test/api/lobbyservice/",
                ["Services:GameService"] = "http://edge.test/api/gameservice/",
                ["Services:MusicService"] = "http://edge.test/api/musicservice/",
                ["Services:StatisticService"] = "http://edge.test/api/statisticservice/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(tokenProvider);
        services.AddServiceLayer(configuration);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => stub));

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ILobbyApiClient>().GetLobbiesAsync();
        await provider.GetRequiredService<IMusicApiClient>().GetPlaylistsAsync();

        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.Requests, r => Assert.Equal("Bearer user-token", r.Headers.Authorization?.ToString()));
        Assert.Equal(new Uri("http://edge.test/api/lobbyservice/lobbies"), stub.Requests[0].RequestUri);
        Assert.Equal(new Uri("http://edge.test/api/musicservice/playlists"), stub.Requests[1].RequestUri);
    }
}
