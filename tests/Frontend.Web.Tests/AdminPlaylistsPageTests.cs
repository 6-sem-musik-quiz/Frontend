using System.Net;

using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Domain.DTOs.Music;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;

using AdminIndex = Frontend.Web.Components.Pages.Admin.Index;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Frontend.Web.Tests;

// SCRUM-114: /admin playlist overview.
public class AdminPlaylistsPageTests : BunitContext
{
    private readonly IMusicApiClient _music = Substitute.For<IMusicApiClient>();

    public AdminPlaylistsPageTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_music);
        Services.AddScoped<ApiCallHandler>();
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()); // /admin reads Grafana:DashboardUrl

        _music.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([
            new PlaylistDto(1, "80s", 12),
            new PlaylistDto(2, "Rock", 0),
            new PlaylistDto(3, "Synthpop", 4)
        ]);
    }

    private static List<string> Names(IRenderedComponent<AdminIndex> cut) =>
        cut.FindAll("[data-testid=playlist-row] td:first-child").Select(td => td.TextContent.Trim()).ToList();

    [Fact]
    public void ShowsNameAndSongCount_WithOpenLink()
    {
        var cut = Render<AdminIndex>();

        Assert.Equal(["80s", "Rock", "Synthpop"], Names(cut));
        var first = cut.FindAll("[data-testid=playlist-row]")[0];
        Assert.Equal("12", first.QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.Equal("admin/playlister/1", first.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void Search_FiltersOnNameIgnoringCase()
    {
        var cut = Render<AdminIndex>();

        cut.Find("#search").Input("ROCK");

        Assert.Equal(["Rock"], Names(cut));
    }

    [Fact]
    public void Create_AddsPlaylistSorted()
    {
        _music.CreatePlaylistAsync("Disco", Arg.Any<CancellationToken>()).Returns(new PlaylistDto(4, "Disco", 0));
        var cut = Render<AdminIndex>();

        cut.Find("#new-playlist").Input(" Disco ");
        cut.Find("[data-testid=new-playlist-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal(["80s", "Disco", "Rock", "Synthpop"], Names(cut)));
        Assert.Equal("", cut.Find("#new-playlist").GetAttribute("value"));
    }

    [Fact]
    public void Create_DuplicateName_ShowsNameTaken()
    {
        _music.CreatePlaylistAsync("Rock", Arg.Any<CancellationToken>()).ThrowsAsync(
            new ApiException(HttpStatusCode.Conflict, "Conflict. A playlist named \"Rock\" already exists.", new ProblemDetailsDto { Status = 409 }));
        var cut = Render<AdminIndex>();

        cut.Find("#new-playlist").Input("Rock");
        cut.Find("[data-testid=new-playlist-form]").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Navnet findes allerede.", cut.Find(".alert-danger").TextContent.Trim()));
    }

    [Fact]
    public void Rename_UpdatesTheRow()
    {
        _music.RenamePlaylistAsync(2, "Classic Rock", Arg.Any<CancellationToken>()).Returns(new PlaylistDto(2, "Classic Rock", 0));
        var cut = Render<AdminIndex>();

        cut.FindAll("[data-testid=playlist-row]")[1].QuerySelectorAll("button")[0].Click(); // Omdøb
        cut.Find("input[aria-label='Nyt navn']").Input("Classic Rock");
        cut.Find("[data-testid=playlist-row] form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(["80s", "Classic Rock", "Synthpop"], Names(cut)));
    }

    [Fact]
    public void Delete_NeedsConfirmation()
    {
        var cut = Render<AdminIndex>();

        cut.FindAll("[data-testid=playlist-row]")[0].QuerySelectorAll("button")[1].Click(); // Slet

        Assert.Contains("80s", cut.Find("[data-testid=confirm-dialog]").TextContent);
        _music.DidNotReceiveWithAnyArgs().DeletePlaylistAsync(default);

        cut.Find("[data-testid=confirm]").Click();

        cut.WaitForAssertion(() => Assert.Equal(["Rock", "Synthpop"], Names(cut)));
        _music.Received(1).DeletePlaylistAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("[data-testid=confirm-dialog]"));
    }

    [Fact]
    public void Delete_Cancel_DeletesNothing()
    {
        var cut = Render<AdminIndex>();

        cut.FindAll("[data-testid=playlist-row]")[0].QuerySelectorAll("button")[1].Click();
        cut.Find("[data-testid=confirm-dialog] .btn-secondary").Click();

        Assert.Empty(cut.FindAll("[data-testid=confirm-dialog]"));
        _music.DidNotReceiveWithAnyArgs().DeletePlaylistAsync(default);
        Assert.Equal(3, Names(cut).Count);
    }
}
