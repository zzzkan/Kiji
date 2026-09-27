using Kiji.Components;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Theory]
    [InlineData(typeof(UnsupportedNavigationPage))]
    [InlineData(typeof(ConstructorNavigationPage))]
    public async Task RenderComponentAsync_MissingNavigationManagerAddsGuidance(Type pageType)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        using var output = new StringWriter();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            renderer.RenderComponentToAsync<KijiRoot>(output, new Dictionary<string, object?>
            {
                [nameof(KijiRoot.PageType)] = pageType,
            }));

        Assert.Contains("NavigationManager", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PageInfo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SiteInfo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.Uri", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ResolveUrl", exception.Message, StringComparison.Ordinal);
        var original = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Microsoft.AspNetCore.Components.NavigationManager", original.Message, StringComparison.Ordinal);
        Assert.Contains(pageType.FullName!, original.Message, StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData("Application error mentioning 'Microsoft.AspNetCore.Components.NavigationManager'.")]
    [InlineData("Cannot provide a value for property 'Clock' on type 'Example'. There is no registered service of type 'System.TimeProvider'.")]
    public async Task RenderComponentAsync_UnrelatedFailuresPreserveOriginalException(string message)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        var original = new InvalidOperationException(message);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderComponentAsync<FailingRenderComponent>(new Dictionary<string, object?>
            {
                [nameof(FailingRenderComponent.Error)] = original,
            }));

        Assert.Same(original, actual);
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
