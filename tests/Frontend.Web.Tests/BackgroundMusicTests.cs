using Frontend.Web.Sound;

namespace Frontend.Web.Tests;

// SCRUM-167: which pages have the quiet menu music.
public class BackgroundMusicTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("?lukket=1", true)]
    [InlineData("lobby/8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11", true)]
    [InlineData("spil/8d3f1a52-0c4e-4f7b-9a61-2b5e7c9d0e11", false)]
    [InlineData("login", false)]
    [InlineData("opret", false)]
    [InlineData("admin", false)]
    public void OnlyTheFrontPageAndTheLobbyHaveMusic(string path, bool expected) =>
        Assert.Equal(expected, BackgroundMusic.HasMusic(path));
}
