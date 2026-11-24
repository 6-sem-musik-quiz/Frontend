using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;

namespace Frontend.Web.Tests;

// SCRUM-153: the deck, the discard pile and the song list. Only our own card data, no music-service links.
public class SongPilesTests : BunitContext
{
    private static PlayedSong Played(int round, int year, bool correct, bool answered) =>
        new(round, new CardDto($"Sang {year}", [$"Kunstner {year}", "Gæst"], year), correct, answered);

    private static readonly List<PlayedSong> Songs =
    [
        Played(1, 1985, correct: true, answered: true),
        Played(2, 2003, correct: false, answered: true),
        Played(3, 1995, correct: false, answered: false)
    ];

    public SongPilesTests() => JSInterop.Mode = JSRuntimeMode.Loose; // focus

    [Theory]
    [InlineData(6, "6", "Bunken: 6 runder tilbage")]
    [InlineData(1, "1", "Bunken: 1 runde tilbage")]
    [InlineData(0, "0", "Bunken: 0 runder tilbage")]
    public void Deck_ShowsHowManyRoundsAreLeft(int left, string count, string label)
    {
        var cut = Render<DeckPile>(p => p.Add(x => x.RoundsLeft, left));

        Assert.Equal(count, cut.Find("[data-testid=deck-count]").TextContent);
        Assert.Equal(label, cut.Find("[data-testid=deck]").GetAttribute("aria-label"));
    }

    [Fact]
    public void Discard_CountsTheSongsIDidNotGet_WrongOrUnanswered()
    {
        var cut = Render<DiscardPile>(p => p.Add(x => x.Songs, Songs));

        Assert.Equal("2", cut.Find("[data-testid=discard-count]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=songs-dialog]"));
    }

    [Fact]
    public void Discard_OpensAListWithYearTitleArtistRoundAndWhy_AndNoLinks()
    {
        var cut = Render<DiscardPile>(p => p.Add(x => x.Songs, Songs));

        cut.Find("[data-testid=discard]").Click();

        var rows = cut.FindAll("[data-testid=song]");
        Assert.Equal(2, rows.Count);
        Assert.Contains("2003", rows[0].TextContent);
        Assert.Contains("Sang 2003", rows[0].TextContent);
        Assert.Contains("Kunstner 2003, Gæst · runde 2", rows[0].TextContent);
        Assert.Contains("forkert", rows[0].TextContent);
        Assert.Contains("intet svar", rows[1].TextContent);
        Assert.Empty(cut.FindAll("[data-testid=songs-dialog] a")); // no links to music services
    }

    [Fact]
    public void Discard_ClosesWithTheButton_AndWithEscape()
    {
        var cut = Render<DiscardPile>(p => p.Add(x => x.Songs, Songs));

        cut.Find("[data-testid=discard]").Click();
        cut.Find("[data-testid=songs-close]").Click();
        Assert.Empty(cut.FindAll("[data-testid=songs-dialog]"));

        cut.Find("[data-testid=discard]").Click();
        cut.Find("[data-testid=songs-dialog]").KeyDown("Escape");
        Assert.Empty(cut.FindAll("[data-testid=songs-dialog]"));
    }

    [Fact]
    public void Discard_WithNothingYet_SaysSo()
    {
        var cut = Render<DiscardPile>(p => p.Add(x => x.Songs, [Played(1, 1985, true, true)]));

        cut.Find("[data-testid=discard]").Click();

        Assert.Equal("Ingen kasserede sange endnu.", cut.Find("[data-testid=songs-empty]").TextContent);
    }

    [Fact]
    public void SongList_ShowsAllSongsInRoundOrder_WithKeptMarked()
    {
        var cut = Render<SongListDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Songs, [Songs[2], Songs[0], Songs[1]]));

        var rows = cut.FindAll("[data-testid=song]");
        Assert.Equal(["runde 1", "runde 2", "runde 3"], rows.Select(r => r.TextContent.Contains("runde 1") ? "runde 1" : r.TextContent.Contains("runde 2") ? "runde 2" : "runde 3"));
        Assert.Contains("i din hånd", rows[0].TextContent);
    }
}
