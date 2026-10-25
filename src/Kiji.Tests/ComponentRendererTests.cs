using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.ErrorHandling;
using Kiji.Tests.TestSite.Head;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kiji.Tests;

public sealed class ComponentRendererTests
{
    [Fact]
    public async Task RenderComponentAsync_ErrorBoundaryRendersAsyncFallbackWithoutLoggingProviders()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        builder.Services.AddLogging(logging => logging.ClearProviders());
        await using var app = builder.Build();
        app.UsePlanningOptions();
        var renderer = new ComponentRenderer(app.ServiceProvider);

        var html = await renderer.RenderComponentAsync<ErrorBoundaryComponent>();

        Assert.Equal("Boundary fallback", html);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/kiji/")]
    [InlineData("https://example.com/日本/")]
    public async Task RenderPageAsync_IsolatesNavigationAndNavLinksAcrossRenders(string baseUrl)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = new SiteInfo { Name = "Navigation", BaseUrl = new Uri(baseUrl) };
        builder.AddPageParameters<NavigationTestPage>(_ => [new { Id = "first" }, new { Id = "日本 語" }]);
        await using var app = builder.Build();
        var pages = app.CreateSnapshot().Pages;

        var html = await Task.WhenAll(pages.Select(page => app.RenderPageAsync(page, CancellationToken.None)));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var url = app.SiteInfo.ResolveUrl(page.RoutePath);
            Assert.Contains(
                $"{app.SiteInfo.BaseUrl.AbsoluteUri}|{url.AbsoluteUri}|{url.AbsoluteUri[app.SiteInfo.BaseUrl.AbsoluteUri.Length..]}|"
                + app.SiteInfo.ResolveUrl("about/").AbsoluteUri + "|about/",
                html[i], StringComparison.Ordinal);
            var active = page.RoutePath == "/navigation/first/" ? " class=\"selected\" aria-current=\"page\"" : "";
            Assert.Contains($"href=\"{app.SiteInfo.ResolveUrl("navigation/first/").AbsolutePath}\"{active}>first</a>",
                html[i], StringComparison.Ordinal);
            Assert.Contains($"href=\"{app.SiteInfo.ResolveUrl("navigation/").AbsolutePath}\" class=\"selected\" aria-current=\"page\">parent</a>",
                html[i], StringComparison.Ordinal);
            Assert.Contains($"href=\"{app.SiteInfo.ResolveUrl("navigation/").AbsolutePath}\">exact</a>",
                html[i], StringComparison.Ordinal);

            Assert.Equal(html[i], await app.RenderPageAsync(page, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RenderPageAsync_IsolatesHeadAcrossParallelAndSequentialRenders()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var app = builder.Build();
        string[] names = ["first", "second"];

        var html = await Task.WhenAll(names.Select(RenderAsync));
        for (var index = 0; index < names.Length; index++)
        {
            Assert.Contains(
                $"<head><title>{names[index]}</title><meta name=\"description\" content=\"{names[index]}\" /></head>",
                html[index], StringComparison.Ordinal);
            Assert.Equal(html[index], await RenderAsync(names[index]));
        }
        Assert.Contains("<head></head>", await RenderAsync(null), StringComparison.Ordinal);

        Task<string> RenderAsync(string? name)
        {
            var route = $"/head/{name ?? "empty"}/";
            return app.RenderPageAsync(new PageRenderRequest(
                route, typeof(HeadTestPage),
                new Dictionary<string, object?> { [nameof(HeadTestPage.Name)] = name },
                route, $"head/{name ?? "empty"}/index.html"), CancellationToken.None);
        }
    }
}
