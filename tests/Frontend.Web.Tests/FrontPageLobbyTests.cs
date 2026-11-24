using System.Net;

using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Domain.DTOs.Lobbies;
using Frontend.Domain.DTOs.Music;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Lobbies;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Frontend.Web.Tests;

// SCRUM-110: create a lobby, see the list, join (also with a code).
public class FrontPageLobbyTests : BunitContext
{
    private static readonly Guid NewLobbyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly IMusicApiClient _music = Substitute.For<IMusicApiClient>();
    private readonly ILobbyApiClient _lobbies = Substitute.For<ILobbyApiClient>();
    private readonly NavigationManager _navigation;

    public FrontPageLobbyTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_music);
        Services.AddSingleton(_lobbies);
        Services.AddScoped<ApiCallHandler>();
        _navigation = Services.GetRequiredService<NavigationManager>();

        _music.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([
            new PlaylistDto(1, "100 hits", 100),
            new PlaylistDto(2, "Lille", 12),   // 12 songs: 5 and 10 rounds only
            new PlaylistDto(3, "Tom", 3)       // too few for any game
        ]);
        _lobbies.CreateLobbyAsync(Arg.Any<CreateLobbyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LobbyDto(NewLobbyId, null, 1, false, new LobbySettingsDto(1, "100 hits", 30, 10, 4), [], "Waiting"));
    }

    private static LobbySummaryDto Summary(string host, int players, int max) =>
        new(Guid.NewGuid(), host, "100 hits", players, max, 10, 30);

    [Fact]
    public void CreateForm_SendsTheChosenValues_AndOpensTheLobby()
    {
        CreateLobbyRequest? sent = null;
        _lobbies.CreateLobbyAsync(Arg.Do<CreateLobbyRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(new LobbyDto(NewLobbyId, "ABC234", 1, true, new LobbySettingsDto(1, "100 hits", 45, 20, 6), [], "Waiting"));
        var cut = Render<CreateLobbyForm>();

        cut.Find("#answer-time").Change("45");
        cut.Find("#rounds").Change("20");
        cut.Find("#max-players").Change("6");
        cut.Find("#private").Change(true);
        cut.Find("[data-testid=create-lobby-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal(new CreateLobbyRequest(1, 45, 20, 6, true), sent));
        Assert.Equal($"http://localhost/lobby/{NewLobbyId}", _navigation.Uri);
    }

    [Fact]
    public void CreateForm_HasTheDefaults()
    {
        var cut = Render<CreateLobbyForm>();

        cut.Find("[data-testid=create-lobby-form]").Submit();

        cut.WaitForAssertion(() =>
            _lobbies.Received(1).CreateLobbyAsync(new CreateLobbyRequest(1, 30, 10, 4, false), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void CreateForm_DisablesRoundsThePlaylistHasTooFewSongsFor()
    {
        var cut = Render<CreateLobbyForm>();

        cut.Find("#playlist").Change("2"); // 12 songs

        var disabled = cut.FindAll("#rounds option").Where(o => o.HasAttribute("disabled")).Select(o => o.TextContent).ToList();
        Assert.Equal(["15", "20"], disabled);
    }

    [Fact]
    public void CreateForm_LowersTheRoundsWhenThePlaylistIsTooSmall()
    {
        var cut = Render<CreateLobbyForm>();
        cut.Find("#rounds").Change("20");

        cut.Find("#playlist").Change("2"); // 12 songs, so at most 10 rounds
        cut.Find("[data-testid=create-lobby-form]").Submit();

        cut.WaitForAssertion(() =>
            _lobbies.Received(1).CreateLobbyAsync(Arg.Is<CreateLobbyRequest>(r => r.CategoryId == 2 && r.Rounds == 10), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void CreateForm_PlaylistWithTooFewSongs_CannotCreate()
    {
        var cut = Render<CreateLobbyForm>();

        cut.Find("#playlist").Change("3");

        Assert.NotNull(cut.Find("[data-testid=too-few-songs]"));
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void CreateForm_AlreadyInALobby_ShowsDanishMessage()
    {
        _lobbies.CreateLobbyAsync(Arg.Any<CreateLobbyRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(
            new ApiException(HttpStatusCode.Conflict, "Conflict. You are already in a lobby.",
                new ProblemDetailsDto { Status = 409, Detail = "You are already in a lobby." }));
        var cut = Render<CreateLobbyForm>();

        cut.Find("[data-testid=create-lobby-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Du er allerede i en lobby. Gå til den eller forlad den øverst på siden.", cut.Find(".alert-danger").TextContent.Trim()));
    }

    [Fact]
    public void List_ShowsRows_AndFullLobbyHasNoJoinButton()
    {
        _lobbies.GetLobbiesAsync(Arg.Any<CancellationToken>()).Returns([Summary("alice", 2, 4), Summary("bob", 4, 4)]);

        var cut = Render<LobbyList>();

        var rows = cut.FindAll("[data-testid=lobby-row]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("2/4", rows[0].QuerySelector("[data-testid=player-count]")!.TextContent);
        Assert.Contains("100 hits · 10 runder · 30 sek.", rows[0].TextContent);
        Assert.NotNull(rows[0].QuerySelector("button"));
        Assert.Null(rows[1].QuerySelector("button"));
        Assert.Contains("Fuld", rows[1].TextContent);
    }

    [Fact]
    public void List_Join_OpensTheLobby()
    {
        var lobby = Summary("alice", 2, 4);
        _lobbies.GetLobbiesAsync(Arg.Any<CancellationToken>()).Returns([lobby]);
        _lobbies.JoinLobbyAsync(lobby.Id, Arg.Any<CancellationToken>())
            .Returns(new LobbyDto(lobby.Id, null, 7, false, new LobbySettingsDto(1, "100 hits", 30, 10, 4), [], "Waiting"));
        var cut = Render<LobbyList>();

        cut.Find("[data-testid=lobby-row] button").Click();

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost/lobby/{lobby.Id}", _navigation.Uri));
    }

    [Fact]
    public void List_JoinByUnknownCode_ShowsError()
    {
        _lobbies.GetLobbiesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _lobbies.JoinByCodeAsync("ABC234", Arg.Any<CancellationToken>()).ThrowsAsync(
            new ApiException(HttpStatusCode.NotFound, "Not found. No lobby has the code 'ABC234'.",
                new ProblemDetailsDto { Status = 404, Detail = "No lobby has the code 'ABC234'." }));
        var cut = Render<LobbyList>();

        cut.Find("#join-code").Input("abc234");
        cut.Find("[data-testid=join-code-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Ingen lobby har den kode.", cut.Find(".alert-danger").TextContent.Trim()));
    }

    [Fact]
    public void List_RefreshesOnItsOwn()
    {
        var calls = 0;
        _lobbies.GetLobbiesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls++;
            return calls == 1 ? [] : [Summary("alice", 1, 4)];
        });

        var cut = Render<LobbyList>(p => p.Add(x => x.RefreshInterval, TimeSpan.FromMilliseconds(50)));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=lobby-row]")), TimeSpan.FromSeconds(2));
    }
}
