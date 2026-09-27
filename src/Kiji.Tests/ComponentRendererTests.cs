using Kiji.Components;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    public static TheoryData<Action<NavigationManager>> NavigationCalls => new()
    {
        navigation => navigation.NavigateTo("target/"),
        navigation => navigation.NavigateTo("target/", forceLoad: true),
        navigation => navigation.NavigateTo("target/", forceLoad: false, replace: true),
        navigation => navigation.NavigateTo("target/", new NavigationOptions
        {
            ForceLoad = true,
            ReplaceHistoryEntry = true,
            HistoryEntryState = "state",
        }),
    };

    [Theory]
    [MemberData(nameof(NavigationCalls))]
    public async Task RenderComponentAsync_NavigateToThrowsActionableError(Action<NavigationManager> navigate)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfoWithBasePath();
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);
        using var output = new StringWriter();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            renderer.RenderComponentToAsync<NavigationTestPage>(output, new Dictionary<string, object?>
            {
                [nameof(NavigationTestPage.InspectNavigation)] = navigate,
            }, new Uri("https://example.com/kiji/navigation/original/")));

        Assert.Contains("NavigationManager.NavigateTo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("target/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("static rendering", exception.Message, StringComparison.Ordinal);
        Assert.Contains("link", exception.Message, StringComparison.Ordinal);
        Assert.Contains("redirect", exception.Message, StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/kiji/")]
    public async Task RenderComponentAsync_PreservesNavigationUriHelpers(string baseUrl)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        var renderer = new ComponentRenderer(app.ServiceProvider, new Uri(baseUrl));

        foreach (var path in new[] { "navigation/first/", "navigation/second/" })
        {
            var currentUri = new Uri(baseUrl + path);
            var html = await renderer.RenderComponentAsync<NavigationTestPage>(new Dictionary<string, object?>
            {
                [nameof(NavigationTestPage.InspectNavigation)] = (Action<NavigationManager>)(navigation =>
                {
                    Assert.Equal(baseUrl, navigation.BaseUri);
                    Assert.Equal(currentUri.AbsoluteUri, navigation.Uri);
                    Assert.Equal(new Uri(baseUrl + "target/"), navigation.ToAbsoluteUri("target/"));
                    Assert.Equal(path, navigation.ToBaseRelativePath(navigation.Uri));
                }),
            }, currentUri);

            Assert.Equal("Original page", html);
        }
    }

    [Fact]
    public async Task RenderComponentAsync_KeepsHeadContentIsolatedAcrossSequentialRenders()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        await using var app = StaticSite.Create([]);
        app.Info = siteInfo;
        var renderer = new ComponentRenderer(app.ServiceProvider, siteInfo.BaseUrl);

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

    private static Dictionary<string, object?> CreateRootParameters(Type pageType) => new()
    {
        [nameof(KijiRoot.PageType)] = pageType,
        [nameof(KijiRoot.PageParameters)] = new Dictionary<string, object?>(),
        [nameof(KijiRoot.DefaultLayout)] = typeof(MainLayout),
    };
}
