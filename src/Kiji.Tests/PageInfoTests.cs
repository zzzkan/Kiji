using Kiji.Feeds;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class PageInfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-page-info-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("https://example.com/", "first", "", "first")]
    [InlineData("https://example.com/kiji/", "second", "kiji/", "second")]
    [InlineData("https://example.com/日本/", "日本 語", "%E6%97%A5%E6%9C%AC/", "%E6%97%A5%E6%9C%AC%20%E8%AA%9E")]
    public async Task RenderPage_ProvidesPublishedUrlForStandardUriConversions(
        string baseUrl, string key, string escapedBasePath, string escapedKey)
    {
        await using var app = CreateApp(baseUrl);
        app.AddPages<PageInfoTestPage>(_ => [new { Key = key }]);
        var request = Assert.Single(app.CreateSnapshot().Pages);

        var html = await app.RenderPageAsync(request, CancellationToken.None);

        Assert.Contains(
            $"https://example.com/{escapedBasePath}page-info/{escapedKey}/"
            + $"|/{escapedBasePath}about/|/{escapedBasePath}page-info/{escapedKey}/image.png|page-info/{escapedKey}/",
            html, StringComparison.Ordinal);

        // The completed render must not leave a page available to an unrelated scope.
        using var scope = app.ServiceProvider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<PageInfo>());
        Assert.Contains("only available during page rendering", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectComponentRender_RequiresPageContext()
    {
        await using var app = CreateApp("https://example.com/");
        var renderer = new ComponentRenderer(app.ServiceProvider);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderComponentAsync<PageInfoTestPage>());
        Assert.Contains("PageInfo is only available during page rendering", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("content")]
    [InlineData("feed")]
    [InlineData("artifact")]
    public async Task PageInfo_CannotBeResolvedOutsidePage(string consumer)
    {
        await using var app = CreateApp("https://example.com/");
        switch (consumer)
        {
            case "route":
                app.AddPages<PageInfoTestPage>(provider =>
                {
                    _ = provider.GetRequiredService<PageInfo>();
                    return [new { Key = "test" }];
                });
                break;
            case "content":
                app.UseContentSource<PageInfo>(provider => [provider.GetRequiredService<PageInfo>()]);
                app.AddPages<PageInfoTestPage>(provider => provider.GetRequiredService<ContentDictionary<PageInfo>>()
                    .Select(entry => new { Key = entry.Key }));
                break;
            case "feed":
                app.AddRssFeed(provider =>
                {
                    _ = provider.GetRequiredService<PageInfo>();
                    return [];
                });
                break;
            case "artifact":
                app.AddArtifact("page.txt", static (_, context, _) =>
                {
                    _ = context.Services.GetRequiredService<PageInfo>();
                    return Task.CompletedTask;
                });
                break;
        }

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("scoped service", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(PageInfo), exception.ToString(), StringComparison.Ordinal);
    }

    private StaticSite CreateApp(string baseUrl)
    {
        var app = StaticSite.Create([]);
        app.Info = new SiteInfo { BaseUrl = new Uri(baseUrl), Name = "Page info" };
        app.Paths.RootDirectory = _root;
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
