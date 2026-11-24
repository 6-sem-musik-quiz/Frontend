using Bunit;

using Frontend.Web.Components.Admin;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontend.Web.Tests;

// SCRUM-117: the "Drift og metrics" card on /admin.
public class GrafanaCardTests : BunitContext
{
    private const string DashboardUrl = "https://grafana.example.test/d/quiz/slopster?refresh=10s";

    private void Configure(string? url) =>
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Grafana:DashboardUrl"] = url })
            .Build());

    [Fact]
    public void WithUrl_ShowsCardWithLinkInNewTab()
    {
        Configure(DashboardUrl);

        var cut = Render<GrafanaCard>();

        Assert.Contains("Drift og metrics", cut.Find("[data-testid=grafana-card]").TextContent);
        Assert.Contains("cache-hits", cut.Markup);
        var link = cut.Find("[data-testid=grafana-card] a");
        Assert.Equal(DashboardUrl, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
        Assert.Contains("Åbn Grafana", link.TextContent);
        Assert.Empty(cut.FindAll("iframe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    public void WithoutValidUrl_ShowsNothing(string? url)
    {
        Configure(url);

        var cut = Render<GrafanaCard>();

        Assert.Empty(cut.FindAll("[data-testid=grafana-card]"));
    }
}
