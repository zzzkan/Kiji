using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Fact]
    public async Task RenderComponentAsync_RejectsNavigationManagerAtInjection()
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        using var output = new StringWriter();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            renderer.RenderComponentToAsync<UnsupportedNavigationPage>(output));

        Assert.Contains("NavigationManager", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PageInfo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SiteInfo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.Uri", exception.Message, StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task RenderPageAsync_KeepsHeadContentIsolatedAcrossSequentialRenders()
    {
        await using var app = StaticSite.Create([]);
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
