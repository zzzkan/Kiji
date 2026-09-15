using Kiji.Components;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Fact]
    public async Task RenderComponentAsync_KeepsHeadContentIsolatedAcrossSequentialRenders()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        await using var renderer = CreateRenderer(siteInfo);

        var indexHtml = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(HomePage)),
            new Uri("https://example.com/"));

        var aboutHtml = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(AboutPage)),
            new Uri("https://example.com/about/"));

        Assert.Contains("<title>Home - zzzkan.me</title>", indexHtml, StringComparison.Ordinal);
        Assert.Contains("<title>About - zzzkan.me</title>", aboutHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>Home - zzzkan.me</title>", aboutHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/about/\"", aboutHtml, StringComparison.Ordinal);
    }

    private static ComponentRenderer CreateRenderer(SiteInfo siteInfo) =>
        ComponentRenderer.Create(services => services.AddSingleton(siteInfo), siteInfo.BaseUrl);

    private static Dictionary<string, object?> CreateRootParameters(Type pageType) => new()
    {
        [nameof(KijiRoot.PageType)] = pageType,
        [nameof(KijiRoot.PageParameters)] = new Dictionary<string, object?>(),
        [nameof(KijiRoot.DefaultLayout)] = typeof(MainLayout),
    };
}
