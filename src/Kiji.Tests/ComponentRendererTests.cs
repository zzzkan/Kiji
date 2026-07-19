using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Kiji.Components;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Fact]
    public async Task RenderComponentAsync_RendersPlainComponentWithParameters()
    {
        await using var renderer = ComponentRenderer.Create();

        var html = await renderer.RenderComponentAsync<TestTextComponent>(new Dictionary<string, object?>
        {
            [nameof(TestTextComponent.Text)] = "hello",
        });

        Assert.Contains("<p>hello</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderComponentAsync_RendersKijiRootWithPageType()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        await using var renderer = CreateRenderer(siteInfo);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(HomePage)),
            new Uri("https://example.com/"));

        Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"ja\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Home - zzzkan.me</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Home</h1>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderComponentAsync_RendersPageWithoutLayoutWhenNoDefaultLayout()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        await using var renderer = CreateRenderer(siteInfo);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(HomePage), new Dictionary<string, object?>(), defaultLayout: null),
            new Uri("https://example.com/"));

        Assert.Contains("<h1>Home</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-header", html, StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task RenderComponentAsync_RendersDiscoveredBlogPageThroughRoot()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();
        var contents = CreateContents();
        var posts = TestArticleContents.CreateCatalog(contents);
        var pageRequest = GetDiscoveredRequest(contents, "/blog/{Slug}/", "Slug", "hello-world");

        await using var renderer = CreateRenderer(posts, siteInfo);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(pageRequest),
            siteInfo.BaseUrl.AppendRelativePath(pageRequest.RoutePath));

        Assert.Contains("<title>Hello World - zzzkan.me</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Hello World</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Hello body</p>", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/blog/hello-world/\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderComponentAsync_RendersDiscoveredTagPageThroughRoot()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();
        var contents = CreateContents();
        var posts = TestArticleContents.CreateCatalog(contents);
        var pageRequest = GetDiscoveredRequest(contents, "/tags/{TagSlug}/", "TagSlug", "c-sharp-basics");

        await using var renderer = CreateRenderer(posts, siteInfo);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(pageRequest),
            siteInfo.BaseUrl.AppendRelativePath(pageRequest.RoutePath));

        Assert.Contains("<title>Tag: C Sharp Basics - zzzkan.me</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Tag: C Sharp Basics</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Hello World", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Post", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/tags/c-sharp-basics/\"", html, StringComparison.Ordinal);
    }

    private static ComponentRenderer CreateRenderer(SiteInfo siteInfo)
    {
        return ComponentRenderer.Create(
            services => services.AddSingleton(siteInfo),
            siteInfo.BaseUrl);
    }

    private static ComponentRenderer CreateRenderer(ContentCollection<Post> posts, SiteInfo siteInfo)
    {
        return ComponentRenderer.Create(
            services =>
            {
                services.AddSingleton(posts);
                services.AddSingleton(siteInfo);
            },
            siteInfo.BaseUrl);
    }

    private static Dictionary<string, object?> CreateRootParameters(Type pageType)
    {
        return CreateRootParameters(pageType, new Dictionary<string, object?>(), typeof(MainLayout));
    }

    private static Dictionary<string, object?> CreateRootParameters(PageRenderRequest pageRequest)
    {
        return CreateRootParameters(pageRequest.ComponentType, pageRequest.Parameters, typeof(MainLayout));
    }

    private static Dictionary<string, object?> CreateRootParameters(
        Type pageType,
        IReadOnlyDictionary<string, object?> pageParameters,
        Type? defaultLayout)
    {
        return new Dictionary<string, object?>
        {
            [nameof(KijiRoot.PageType)] = pageType,
            [nameof(KijiRoot.PageParameters)] = pageParameters,
            [nameof(KijiRoot.DefaultLayout)] = defaultLayout,
        };
    }

    private static PageRenderRequest GetDiscoveredRequest(
        (Post Metadata, string Html)[] contents,
        string sourceIdentifier,
        string parameterName,
        string parameterValue)
    {
        return Assert.Single(
            TestArticleContents.CreatePageRequests(contents),
            request =>
                string.Equals(request.SourceIdentifier, sourceIdentifier, StringComparison.Ordinal) &&
                string.Equals(request.Parameters[parameterName] as string, parameterValue, StringComparison.Ordinal));
    }

    private static (Post Metadata, string Html)[] CreateContents()
    {
        return
        [
            (TestArticleContents.CreatePost(
                "hello-world",
                "Hello World",
                "Hello world description",
                new DateOnly(2026, 3, 18),
                new DateOnly(2026, 3, 19),
                "C Sharp Basics",
                "Testing"), "<p>Hello body</p>"),
            (TestArticleContents.CreatePost(
                "other-post",
                "Other Post",
                "Other post description",
                new DateOnly(2026, 3, 17),
                null,
                "DotNet"), "<p>Other body</p>"),
        ];
    }

    private sealed class TestTextComponent : ComponentBase
    {
        [Parameter]
        public string Text { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, Text);
            builder.CloseElement();
        }
    }
}
