using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/kiji/")]
    [InlineData("https://example.com/日本/")]
    public async Task RenderPageAsync_IsolatesNavigationAndNavLinksAcrossRenders(string baseUrl)
    {
        await using var app = StaticSite.Create([], new SiteExecutionPaths(AppContext.BaseDirectory));
        app.Info = new SiteInfo { Name = "Navigation", BaseUrl = new Uri(baseUrl) };
        app.AddPages<NavigationTestPage>(_ => [new { Id = "first" }, new { Id = "日本 語" }]);
        var pages = app.CreateSnapshot().Pages;

        var html = await Task.WhenAll(pages.Select(page => app.RenderPageAsync(page, CancellationToken.None)));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var url = app.Info.ResolveUrl(page.RoutePath);
            Assert.Contains(
                $"{app.Info.BaseUrl.AbsoluteUri}|{url.AbsoluteUri}|{url.AbsoluteUri[app.Info.BaseUrl.AbsoluteUri.Length..]}|"
                + app.Info.ResolveUrl("about/").AbsoluteUri + "|about/",
                html[i], StringComparison.Ordinal);
            var active = page.RoutePath == "/navigation/first/" ? " class=\"selected\" aria-current=\"page\"" : "";
            Assert.Contains($"href=\"{app.Info.ResolveUrl("navigation/first/").AbsolutePath}\"{active}>first</a>",
                html[i], StringComparison.Ordinal);
            Assert.Contains($"href=\"{app.Info.ResolveUrl("navigation/").AbsolutePath}\" class=\"selected\" aria-current=\"page\">parent</a>",
                html[i], StringComparison.Ordinal);
            Assert.Contains($"href=\"{app.Info.ResolveUrl("navigation/").AbsolutePath}\">exact</a>",
                html[i], StringComparison.Ordinal);

            Assert.Equal(html[i], await app.RenderPageAsync(page, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RenderPageAsync_KeepsHeadContentIsolatedAcrossSequentialRenders()
    {
        await using var app = StaticSite.Create([], new SiteExecutionPaths(AppContext.BaseDirectory));
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseDefaultLayout<MainLayout>();

        var indexHtml = await app.RenderPageAsync(new PageRenderRequest(
            "/", typeof(HomePage), new Dictionary<string, object?>(), "/", "index.html"), CancellationToken.None);
        var aboutHtml = await app.RenderPageAsync(new PageRenderRequest(
            "/about/", typeof(AboutPage), new Dictionary<string, object?>(), "/about/", "about/index.html"), CancellationToken.None);

        Assert.Contains("<title>Home - zzzkan.me</title>", indexHtml, StringComparison.Ordinal);
        Assert.Contains("<title>About - zzzkan.me</title>", aboutHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>Home - zzzkan.me</title>", aboutHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/about/\"", aboutHtml, StringComparison.Ordinal);
    }
}
