using Frontend.Web.Auth;

namespace Frontend.Web.Tests;

public class SessionAccessTokenProviderTests
{
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-11-20T12:00:00Z"));
    private readonly UserSession _session = new();

    [Fact]
    public void ReturnsToken_WhenSignedIn()
    {
        var token = TestTokens.Create("bob", "Player", _time.GetUtcNow().AddHours(1));
        _session.Set(JwtTokenParser.TryParse(token));

        Assert.Equal(token, new SessionAccessTokenProvider(_session, _time).GetAccessToken());
    }

    [Fact]
    public void ReturnsNull_WhenTokenExpired()
    {
        _session.Set(JwtTokenParser.TryParse(TestTokens.Create("bob", "Player", _time.GetUtcNow().AddMinutes(-1))));

        Assert.Null(new SessionAccessTokenProvider(_session, _time).GetAccessToken());
    }

    [Fact]
    public void ReturnsNull_WhenAnonymous()
    {
        Assert.Null(new SessionAccessTokenProvider(_session, _time).GetAccessToken());
    }
}
