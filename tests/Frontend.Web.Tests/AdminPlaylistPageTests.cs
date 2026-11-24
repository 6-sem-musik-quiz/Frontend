using System.Net;

using Bunit;

using Frontend.Domain.DTOs;
using Frontend.Domain.DTOs.Music;
using Frontend.Services.Errors;
using Frontend.Services.Interfaces;
using Frontend.Web.Auth;
using Frontend.Web.Components.Pages.Admin;
using Frontend.Web.Music;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Frontend.Web.Tests;

// SCRUM-115: /admin/playlister/{id}: songs, upload, edit and remove.
public class AdminPlaylistPageTests : BunitContext
{
    private static readonly SongDto TakeOnMe = new(1, "Take On Me", ["a-ha"], 1985, 225, "https://media.munkcloud.dk/songs/a.mp3");
    private static readonly SongDto UnderPressure = new(2, "Under Pressure", ["David Bowie", "Queen"], 1981, 248, "https://media.munkcloud.dk/songs/b.mp3");
    private static readonly SongDto Africa = new(3, "Africa", ["Toto"], 1982, 295, "https://media.munkcloud.dk/songs/c.mp3");

    private readonly IMusicApiClient _music = Substitute.For<IMusicApiClient>();
    private readonly ISongUploader _uploader = Substitute.For<ISongUploader>();

    public AdminPlaylistPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // InputFile initialises itself through JS
        Services.AddLogging();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(_music);
        Services.AddSingleton(_uploader);
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<ApiCallHandler>();

        _music.GetPlaylistAsync(7, Arg.Any<CancellationToken>()).Returns(new PlaylistDto(7, "80s", 3));
        _music.GetSongsAsync(7, Arg.Any<CancellationToken>()).Returns([TakeOnMe, UnderPressure, Africa]);
        _music.SearchArtistsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            ArtistDto[] all = [new(10, "a-ha"), new(11, "David Bowie"), new(12, "Queen"), new(13, "Toto")];
            var search = c.Arg<string?>() ?? "";
            return all.Where(a => a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        });
    }

    private IRenderedComponent<Playlist> RenderPage() =>
        Render<Playlist>(p => p.Add(x => x.Id, 7));

    private static List<string> Titles(IRenderedComponent<Playlist> cut) =>
        cut.FindAll("[data-testid=song-row] td:first-child").Select(td => td.TextContent.Trim()).ToList();

    [Fact]
    public void ShowsHeaderAndSongTable()
    {
        var cut = RenderPage();

        Assert.Equal("80s", cut.Find("h1").TextContent);
        Assert.Equal("3 sange", cut.Find("[data-testid=song-count]").TextContent);
        var row = cut.FindAll("[data-testid=song-row]")[2]; // Under Pressure, sorted by title
        var cells = row.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList();
        Assert.Equal(["Under Pressure", "David Bowie, Queen", "1981", "4:08"], cells.Take(4));
    }

    [Fact]
    public void SortsByTitleAndYear()
    {
        var cut = RenderPage();
        Assert.Equal(["Africa", "Take On Me", "Under Pressure"], Titles(cut));

        cut.Find("[data-testid=sort-title]").Click();
        Assert.Equal(["Under Pressure", "Take On Me", "Africa"], Titles(cut));

        cut.Find("[data-testid=sort-year]").Click();
        Assert.Equal(["Under Pressure", "Africa", "Take On Me"], Titles(cut));
    }

    [Fact]
    public void Search_MatchesTitleOrArtist()
    {
        var cut = RenderPage();

        cut.Find("#song-search").Input("queen");
        Assert.Equal(["Under Pressure"], Titles(cut));

        cut.Find("#song-search").Input("take");
        Assert.Equal(["Take On Me"], Titles(cut));
    }

    [Fact]
    public void PlayButton_ShowsPlayerWithAudioUrl()
    {
        var cut = RenderPage();

        cut.Find("button[aria-label='Afspil Africa']").Click();

        Assert.Equal(Africa.AudioUrl, cut.Find("[data-testid=player] audio").GetAttribute("src"));
    }

    [Fact]
    public void Upload_SendsFieldsAndAddsTheSong()
    {
        var uploaded = new SongDto(9, "Heroes", ["David Bowie"], 1977, 371, "https://media.munkcloud.dk/songs/d.mp3");
        UploadSongRequest? sent = null;
        _uploader.UploadAsync(7, Arg.Any<ElementReference>(), Arg.Do<UploadSongRequest>(r => sent = r), Arg.Any<Action<int>?>())
            .Returns(uploaded);
        var cut = RenderPage();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "heroes.mp3", contentType: "audio/mpeg"));
        Assert.Equal("heroes", cut.Find("#upload-title").GetAttribute("value")); // from the file name
        cut.Find("#upload-title").Input("Heroes");
        PickArtist(cut, "#upload-artist", "bowie", "David Bowie");
        cut.Find("#upload-year").Change(1977);
        cut.Find("[data-testid=upload-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Heroes", Titles(cut)));
        Assert.Equal("Heroes", sent!.Title);
        Assert.Equal(1977, sent.Year);
        Assert.Equal([11], sent.ArtistIds);
        Assert.Equal("4 sange", cut.Find("[data-testid=song-count]").TextContent);
    }

    [Fact]
    public void Upload_WithoutArtist_ShowsMessageAndSendsNothing()
    {
        var cut = RenderPage();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "song.mp3", contentType: "audio/mpeg"));
        cut.Find("#upload-year").Change(1985);
        cut.Find("[data-testid=upload-form]").Submit();

        Assert.Equal("Vælg mindst én kunstner.", cut.Find("[data-testid=upload-form]").ParentElement!.QuerySelector(".alert-danger")!.TextContent.Trim());
        _uploader.DidNotReceiveWithAnyArgs().UploadAsync(default, default, default!, default);
    }

    [Fact]
    public void Upload_NotAnMp3_IsRejectedBeforeSending()
    {
        var cut = RenderPage();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "song.wav", contentType: "audio/wav"));
        PickArtist(cut, "#upload-artist", "toto", "Toto");
        cut.Find("#upload-year").Change(1982);
        cut.Find("[data-testid=upload-form]").Submit();

        Assert.Contains("Filen er ikke en mp3.", cut.Markup);
        _uploader.DidNotReceiveWithAnyArgs().UploadAsync(default, default, default!, default);
    }

    [Fact]
    public void Upload_ServerError_IsShown()
    {
        _uploader.UploadAsync(7, Arg.Any<ElementReference>(), Arg.Any<UploadSongRequest>(), Arg.Any<Action<int>?>())
            .ThrowsAsync(new ApiException(HttpStatusCode.BadRequest, "Invalid request. file is not an mp3, or its length cannot be read.",
                new ProblemDetailsDto { Status = 400 }));
        var cut = RenderPage();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "song.mp3", contentType: "audio/mpeg"));
        PickArtist(cut, "#upload-artist", "toto", "Toto");
        cut.Find("#upload-year").Change(1982);
        cut.Find("[data-testid=upload-form]").Submit();

        cut.WaitForAssertion(() => Assert.Contains("file is not an mp3", cut.Markup));
        Assert.Equal(3, Titles(cut).Count);
    }

    [Fact]
    public void Edit_PrefillsArtistsAndSavesWithIds()
    {
        var updated = UnderPressure with { Title = "Under Pressure (Remastered)", Year = 1982 };
        _music.UpdateSongAsync(2, Arg.Any<UpdateSongRequest>(), Arg.Any<CancellationToken>()).Returns(updated);
        var cut = RenderPage();

        RowButton(cut, "Under Pressure", "Ret").Click();
        cut.WaitForAssertion(() =>
            Assert.Equal(["David Bowie", "Queen"], cut.FindAll("[data-testid=edit-dialog] [data-testid=selected-artists] .badge")
                .Select(b => b.TextContent.Trim()).ToList()));
        Assert.Contains("alle playlister", cut.Find("[data-testid=edit-dialog]").TextContent);

        cut.Find("#edit-title").Input("Under Pressure (Remastered)");
        cut.Find("#edit-year").Change(1982);
        cut.Find("[data-testid=edit-dialog] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Under Pressure (Remastered)", Titles(cut)));
        _music.Received(1).UpdateSongAsync(2,
            Arg.Is<UpdateSongRequest>(r => r.Title == "Under Pressure (Remastered)" && r.Year == 1982 && r.ArtistIds.SequenceEqual(new[] { 11, 12 })),
            Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("[data-testid=edit-dialog]"));
    }

    [Fact]
    public void Remove_NeedsConfirmationAndRemovesTheRow()
    {
        var cut = RenderPage();

        RowButton(cut, "Africa", "Fjern").Click();
        Assert.Contains("slettes helt, hvis den ikke er i andre playlister", cut.Find("[data-testid=confirm-dialog]").TextContent);
        _music.DidNotReceiveWithAnyArgs().RemoveSongAsync(default, default);

        cut.Find("[data-testid=confirm]").Click();

        cut.WaitForAssertion(() => Assert.Equal(["Take On Me", "Under Pressure"], Titles(cut)));
        _music.Received(1).RemoveSongAsync(7, 3, Arg.Any<CancellationToken>());
        Assert.Equal("2 sange", cut.Find("[data-testid=song-count]").TextContent);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    public void FormatDuration_IsMinutesAndSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, Playlist.FormatDuration(seconds));
    }

    private static void PickArtist(IRenderedComponent<Playlist> cut, string input, string search, string name)
    {
        cut.Find(input).Input(search);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=artist-results] button").Single(b => b.TextContent.Trim() == name).Click());
    }

    private static AngleSharp.Dom.IElement RowButton(IRenderedComponent<Playlist> cut, string title, string button) =>
        cut.FindAll("[data-testid=song-row]")
            .Single(r => r.QuerySelector("td")!.TextContent.Trim() == title)
            .QuerySelectorAll("button")
            .Single(b => b.TextContent.Trim() == button);
}
