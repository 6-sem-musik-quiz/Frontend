using Bunit;

using Frontend.Web.Games;

namespace Frontend.Web.Tests;

// SCRUM-147: Danish texts for GameService's hub errors.
public class GameMessagesTests
{
    private static string AsClientSees(string serverMessage) =>
        $"An unexpected error occurred invoking 'SubmitAnswer' on the server. HubException: {serverMessage}";

    [Theory]
    [InlineData("The game does not exist.", "Spillet findes ikke (længere).")]
    [InlineData("You are not a player in this game.", "Du er ikke med i det her spil.")]
    [InlineData("That round is not being played.", "Runden er slut.")]
    [InlineData("The time is up.", "Tiden er gået.")]
    [InlineData("You have already answered.", "Du har allerede svaret.")]
    [InlineData("The token has no valid user id.", "Du skal logge ind igen.")]
    [InlineData("slotIndex must be between 0 and 3.", "Vælg en plads i din tidslinje.")]
    [InlineData("Round must be between 1 and 10.", "Den runde findes ikke i spillet.")]
    public void For_MapsGameServiceMessages(string server, string danish)
    {
        Assert.Equal(danish, GameMessages.For(AsClientSees(server)));
        Assert.Equal(danish, GameMessages.For(server)); // also without SignalR's prefix
    }

    [Theory]
    [InlineData("An unexpected error occurred invoking 'Ready' on the server.")] // server-side bug, no HubException
    [InlineData("Something new")]
    [InlineData("")]
    [InlineData(null)]
    public void For_UnknownMessages_GiveTheGeneralText(string? message) =>
        Assert.Equal(GameMessages.General, GameMessages.For(message));
}

// SCRUM-147: GameAudio only forwards to wwwroot/js/gameAudio.js.
public class GameAudioTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;

    public GameAudioTests() => _module = JSInterop.SetupModule("./js/gameAudio.js");

    private GameAudio Audio() => new(JSInterop.JSRuntime);

    [Fact]
    public async Task Prefetch_ReturnsTheBlobUrl_OrNullWhenTheDownloadFailed()
    {
        _module.Setup<string?>("prefetch", "https://media.munkcloud.dk/songs/1.mp3").SetResult("blob:http://localhost/abc");
        _module.Setup<string?>("prefetch", "https://media.munkcloud.dk/songs/2.mp3").SetResult(null);
        var audio = Audio();

        Assert.Equal("blob:http://localhost/abc", await audio.PrefetchAsync("https://media.munkcloud.dk/songs/1.mp3"));
        Assert.Null(await audio.PrefetchAsync("https://media.munkcloud.dk/songs/2.mp3"));
    }

    [Fact]
    public async Task Play_ReturnsFalse_WhenTheBrowserBlocksAutoplay()
    {
        _module.Setup<bool>("play", "blob:x", 42.0).SetResult(false);

        Assert.False(await Audio().PlayAsync("blob:x", 42));
    }

    [Fact]
    public async Task SetVolume_IsClampedToZeroToOne()
    {
        _module.SetupVoid("setVolume", _ => true).SetVoidResult();
        var audio = Audio();

        await audio.SetVolumeAsync(1.7);
        await audio.SetVolumeAsync(-0.2);

        Assert.Equal([1.0, 0.0], _module.Invocations["setVolume"].Select(i => (double)i.Arguments[0]!));
    }
}
