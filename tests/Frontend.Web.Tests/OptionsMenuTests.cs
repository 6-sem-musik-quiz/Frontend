using Bunit;

using Frontend.Web.Components.Layout;
using Frontend.Web.Components.Pages;
using Frontend.Web.Games;
using Frontend.Web.Sound;

using Microsoft.Extensions.DependencyInjection;

namespace Frontend.Web.Tests;

// SCRUM-166: the options menu in the top bar sets the music and the sound effects separately.
public class OptionsMenuTests : BunitContext
{
    private readonly FakeSoundEffects _sounds = new();
    private readonly FakeGameAudio _audio = new();

    public OptionsMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // localStorage
        Services.AddSingleton<ISoundEffects>(_sounds);
        Services.AddSingleton<IGameAudio>(_audio);
        Services.AddSingleton<IBackgroundMusic>(_music);
    }

    private readonly FakeBackgroundMusic _music = new();

    private IRenderedComponent<OptionsMenu> Open()
    {
        var cut = Render<OptionsMenu>();
        cut.Find("[data-testid=options-button]").Click();
        return cut;
    }

    [Fact]
    public void IsClosed_UntilTheButtonIsClicked_AndShowsTheStoredLevels()
    {
        JSInterop.Setup<string?>("localStorage.getItem", GamePage.VolumeKey).SetResult("0.3");
        _sounds.SetVolumeAsync(0.7);
        var cut = Render<OptionsMenu>();
        Assert.Empty(cut.FindAll("[data-testid=options-menu]"));

        cut.Find("[data-testid=options-button]").Click();

        Assert.Equal("30 %", cut.Find("[data-testid=options-music-value]").TextContent);
        Assert.Equal("70 %", cut.Find("[data-testid=options-effects-value]").TextContent);
    }

    [Fact]
    public void Music_SetsTheSongVolume_AndStoresIt_WithoutTouchingTheEffects()
    {
        var cut = Open();

        cut.Find("[data-testid=options-music]").Input("25");

        Assert.Equal(0.25, _audio.Volumes[^1], precision: 3);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem"
                                                    && (string?)i.Arguments[0] == GamePage.VolumeKey
                                                    && (string?)i.Arguments[1] == "0.25");
        Assert.Null(_sounds.Volume);
        Assert.Equal(1, _music.Refreshes); // the menu music follows "Musik"
    }

    [Fact]
    public void Effects_SetsTheEffectsVolume_AndPlaysATick()
    {
        var cut = Open();

        cut.Find("[data-testid=options-effects]").Change("90");

        Assert.Equal(0.9, _sounds.Volume!.Value, precision: 3);
        Assert.Equal([Sfx.Tick], _sounds.Played);
        Assert.Empty(_audio.Volumes);
    }

    [Fact]
    public void Mute_TurnsTheEffectsOff_AndDisablesTheSlider()
    {
        var cut = Open();

        cut.Find("[data-testid=options-mute]").Change(true);

        Assert.True(_sounds.Muted);
        Assert.True(cut.Find("[data-testid=options-effects]").HasAttribute("disabled"));
        Assert.Equal("Slået fra", cut.Find("[data-testid=options-effects-value]").TextContent);
    }

    [Fact]
    public void Close_HidesTheMenu()
    {
        var cut = Open();

        cut.Find("[data-testid=options-close]").Click();

        Assert.Empty(cut.FindAll("[data-testid=options-menu]"));
    }
}
