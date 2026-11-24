using Bunit;

using Frontend.Domain.DTOs.Games;
using Frontend.Web.Components.Game;

using Microsoft.AspNetCore.Components;

namespace Frontend.Web.Tests;

// SCRUM-148: the hand as timeline. Drag and drop itself runs in hand.js; here its calls into .NET
// (DragStarted, HotSlot, Dropped) are made directly, like the script does.
public class HandOfCardsTests : BunitContext
{
    private static readonly List<CardDto> Timeline =
    [
        new("Dancing Queen", ["ABBA"], 1976),
        new("Take On Me", ["a-ha"], 1985),
        new("Smells Like Teen Spirit", ["Nirvana"], 1991)
    ];

    private static readonly CardDto Song = new("Billie Jean", ["Michael Jackson"], 1982);

    private readonly List<int> _confirmed = [];

    public HandOfCardsTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<HandOfCards> RenderHand(int round = 4, bool locked = false, RoundReveal? reveal = null) =>
        Render<HandOfCards>(p => p
            .Add(x => x.Cards, Timeline)
            .Add(x => x.Round, round)
            .Add(x => x.Locked, locked)
            .Add(x => x.Reveal, reveal)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create<int>(this, slot => _confirmed.Add(slot))));

    // Position of the placed "?" among the hand's children: 0 = before the first card.
    private static int? PlacedSlot(IRenderedComponent<HandOfCards> cut)
    {
        var hand = cut.Find("[data-hand]");
        var slot = 0;
        foreach (var child in hand.Children)
        {
            if (child.GetAttribute("data-testid") == "placed") return slot;
            if (child.GetAttribute("data-testid") == "card") slot++;
        }
        return null;
    }

    private static string Message(IRenderedComponent<HandOfCards> cut) => cut.Find("[data-testid=message]").TextContent.Trim();

    [Fact]
    public void Shows_TheCardsInOrder_AndOneGapMoreThanCards_WithReadableLabels()
    {
        var cut = RenderHand();

        Assert.Equal(["1976", "1985", "1991"], cut.FindAll("[data-testid=card] .gc-year").Select(e => e.TextContent));
        Assert.Equal(["Før 1976", "Mellem 1976 og 1985", "Mellem 1985 og 1991", "Efter 1991"],
            cut.FindAll("[data-testid=slot]").Select(s => s.GetAttribute("aria-label")));
        Assert.Equal("Din hånd · 3 kort = 3 point", cut.Find("[data-testid=count]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=mystery]").GetAttribute("data-drag"));
    }

    [Fact]
    public void ClickingAGap_PlacesTheCard_AndConfirmSendsThatSlot()
    {
        var cut = RenderHand();

        cut.FindAll("[data-testid=slot]")[2].Click();
        Assert.Equal(2, PlacedSlot(cut));
        Assert.Empty(cut.FindAll("[data-testid=mystery]"));
        Assert.Equal("Placeret – flyt eller bekræft", Message(cut));

        cut.FindAll("[data-testid=slot]")[0].Click(); // can still be moved
        cut.Find("[data-testid=confirm]").Click();

        Assert.Equal([0], _confirmed);
    }

    [Fact]
    public void Keyboard_ArrowsMoveTheCard_AndEnterConfirms()
    {
        var cut = RenderHand();

        cut.Find("[data-testid=mystery]").KeyDown("ArrowRight"); // first press: before the first card
        Assert.Equal(0, PlacedSlot(cut));
        cut.Find("[data-testid=placed]").KeyDown("ArrowRight");
        cut.Find("[data-testid=placed]").KeyDown("ArrowRight");
        Assert.Equal(2, PlacedSlot(cut));
        cut.Find("[data-testid=placed]").KeyDown("End");
        Assert.Equal(3, PlacedSlot(cut));
        cut.Find("[data-testid=placed]").KeyDown("ArrowLeft");
        cut.Find("[data-testid=placed]").KeyDown("Enter");

        Assert.Equal([2], _confirmed);
    }

    [Fact]
    public void Keyboard_ArrowLeft_WithoutPlacement_StartsAtTheEnd()
    {
        var cut = RenderHand();

        cut.Find("[data-testid=mystery]").KeyDown("ArrowLeft");

        Assert.Equal(3, PlacedSlot(cut));
    }

    [Fact]
    public void Confirm_WithoutPlacement_AsksToPlaceFirst_AndSendsNothing()
    {
        var cut = RenderHand();

        cut.Find("[data-testid=confirm]").Click();

        Assert.Equal("Placér kortet først", Message(cut));
        Assert.Empty(_confirmed);
        Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
    }

    [Fact]
    public void AfterConfirm_EverythingIsLocked()
    {
        var cut = RenderHand();
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();

        Assert.Equal("Svar afgivet – låst", Message(cut));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.All(cut.FindAll("[data-testid=slot]"), s => Assert.True(s.HasAttribute("disabled")));
        Assert.Null(cut.Find("[data-testid=placed]").GetAttribute("data-drag"));

        cut.Find("[data-testid=placed]").KeyDown("ArrowLeft");
        cut.Instance.DragStarted();
        Assert.Equal(1, PlacedSlot(cut));
        Assert.Equal([1], _confirmed);
    }

    [Fact]
    public void WhenTimeIsUp_NothingCanBePlaced()
    {
        var cut = RenderHand(locked: true);

        Assert.Equal("Tiden er gået.", Message(cut));
        Assert.True(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.Null(cut.Find("[data-testid=mystery]").GetAttribute("data-drag"));
        cut.Find("[data-testid=mystery]").KeyDown("ArrowRight");
        Assert.Null(PlacedSlot(cut));
    }

    [Fact]
    public async Task Dragging_OpensTheHotGap_AndDroppingPlacesTheCardThere()
    {
        var cut = RenderHand();

        await cut.InvokeAsync(() => cut.Instance.DragStarted());
        Assert.Equal("Træk ned mod hånden", cut.Find("[data-testid=hint]").TextContent);
        await cut.InvokeAsync(() => cut.Instance.HotSlot(2));
        Assert.Contains("hot", cut.FindAll("[data-testid=slot]")[2].ClassList);
        Assert.Equal("Slip for at lægge kortet her", cut.Find("[data-testid=hint]").TextContent);

        await cut.InvokeAsync(() => cut.Instance.Dropped(2));

        Assert.Equal(2, PlacedSlot(cut));
        Assert.DoesNotContain(cut.FindAll("[data-testid=slot]"), s => s.ClassList.Contains("hot"));
    }

    [Fact]
    public async Task DroppingOutsideTheHand_PutsTheCardBackWhereItWas()
    {
        var cut = RenderHand();
        cut.FindAll("[data-testid=slot]")[1].Click();

        await cut.InvokeAsync(() => cut.Instance.DragStarted()); // grabbed the placed card again
        Assert.Null(PlacedSlot(cut));                             // it follows the pointer, out of the hand
        await cut.InvokeAsync(() => cut.Instance.Dropped(-1));

        Assert.Equal(1, PlacedSlot(cut));
    }

    [Fact]
    public void RightAnswer_TurnsTheCardIntoTheSong_AndKeepsIt()
    {
        var cut = RenderHand();
        cut.FindAll("[data-testid=slot]")[2].Click();
        cut.Find("[data-testid=confirm]").Click();

        cut.Render(p => p.Add(x => x.Reveal, new RoundReveal(Song, Answered: true, Correct: true)));

        var placed = cut.Find("[data-testid=placed]");
        Assert.Contains("gc-new", placed.ClassList);
        Assert.Equal("1982", placed.QuerySelector(".gc-year")!.TextContent);
        Assert.Contains("Billie Jean", placed.TextContent);
        Assert.Equal("Rigtigt! Kortet bliver i din hånd.", Message(cut));
    }

    [Fact]
    public void WrongAnswer_RemovesTheCard()
    {
        var cut = RenderHand();
        cut.FindAll("[data-testid=slot]")[0].Click();
        cut.Find("[data-testid=confirm]").Click();

        cut.Render(p => p.Add(x => x.Reveal, new RoundReveal(Song, Answered: true, Correct: false)));

        Assert.Contains("gc-out", cut.Find("[data-testid=placed]").ClassList);
        Assert.DoesNotContain("1982", cut.Find("[data-testid=placed]").TextContent); // the song is not shown in the hand
        Assert.Equal("Forkert – kortet ryger ud.", Message(cut));
    }

    [Fact]
    public void NoAnswer_LeavesTheQuestionMarkOutsideTheHand()
    {
        var cut = RenderHand();
        cut.FindAll("[data-testid=slot]")[0].Click(); // placed but never confirmed

        cut.Render(p => p.Add(x => x.Reveal, new RoundReveal(Song, Answered: false, Correct: false)));

        Assert.Null(PlacedSlot(cut));
        Assert.Contains("gc-missed", cut.Find("[data-testid=mystery]").ClassList);
        Assert.Equal("Intet svar – tiden løb ud.", Message(cut));
    }

    [Fact]
    public void ANewRound_ResetsPlacementAndLock()
    {
        var cut = RenderHand(round: 4);
        cut.FindAll("[data-testid=slot]")[1].Click();
        cut.Find("[data-testid=confirm]").Click();

        cut.Render(p => p.Add(x => x.Round, 5).Add(x => x.Reveal, null));

        Assert.Null(PlacedSlot(cut));
        Assert.False(cut.Find("[data-testid=confirm]").HasAttribute("disabled"));
        Assert.Equal("", Message(cut));
    }

    [Fact]
    public void AnEmptyHand_HasOneGap()
    {
        var cut = Render<HandOfCards>(p => p.Add(x => x.Cards, Array.Empty<CardDto>()));

        Assert.Equal("Læg kortet her", Assert.Single(cut.FindAll("[data-testid=slot]")).GetAttribute("aria-label"));
    }
}

// SCRUM-148: the left column.
public class GameSidebarTests : BunitContext
{
    [Fact]
    public void ShowsRoundTimeAndPlayers_WithOnlyCardCountsAndAnsweredMarks()
    {
        var cut = Render<GameSidebar>(p => p
            .Add(x => x.Round, 4).Add(x => x.TotalRounds, 10)
            .Add(x => x.SecondsLeft, 15).Add(x => x.AnswerTimeSeconds, 30)
            .Add(x => x.MyUserId, 1)
            .Add(x => x.Players, [new GamePlayerView(1, "Mathias", 5, false), new GamePlayerView(2, "Asger", 4, true)]));

        Assert.Equal("4 / 10", cut.Find("[data-testid=round]").TextContent);
        Assert.Equal("15", cut.Find("[data-testid=time]").TextContent);
        Assert.Equal("width:50%", cut.Find("[data-testid=time-bar]").GetAttribute("style"));

        var players = cut.FindAll("[data-testid=game-player]");
        Assert.Contains("me", players[0].ClassList);
        Assert.Null(players[0].QuerySelector("[data-testid=answered]"));
        Assert.NotNull(players[1].QuerySelector("[data-testid=answered]"));
        Assert.Equal(["5", "4"], players.Select(p => p.QuerySelector("b")!.TextContent));
    }

    [Fact]
    public void BeforePlayback_ShowsADash_AndAnEmptyBar()
    {
        var cut = Render<GameSidebar>(p => p.Add(x => x.SecondsLeft, null));

        Assert.Equal("–", cut.Find("[data-testid=time]").TextContent);
        Assert.Equal("width:0%", cut.Find("[data-testid=time-bar]").GetAttribute("style"));
    }
}
