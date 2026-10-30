using Kiji.Feeds;
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
        var builder = CreateBuilder(baseUrl);
        builder.AddPageParameters<PageInfoTestPage>(_ => [new { Key = key }]);
        await using var app = builder.Build();
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

    [Theory]
    [InlineData("route")]
    [InlineData("content")]
    [InlineData("feed")]
    [InlineData("artifact")]
    public async Task PageInfo_CannotBeResolvedOutsidePage(string consumer)
    {
        var builder = CreateBuilder("https://example.com/");
        switch (consumer)
        {
            case "route":
                builder.AddPageParameters<PageInfoTestPage>(provider =>
                {
                    _ = provider.GetRequiredService<PageInfo>();
                    return [new { Key = "test" }];
                });
                break;
            case "content":
                builder.AddContentSource<PageInfo>(provider => [provider.GetRequiredService<PageInfo>()]);
                builder.AddPageParameters<PageInfoTestPage>(provider => provider.GetRequiredService<ContentDictionary<PageInfo>>()
                    .Select(entry => new { Key = entry.Key }));
                break;
            case "feed":
                builder.AddRssFeed(provider =>
                {
                    _ = provider.GetRequiredService<PageInfo>();
                    return [];
                });
                break;
            case "artifact":
                builder.AddArtifact("page.txt", static (_, context, _) =>
                {
                    _ = context.Services.GetRequiredService<PageInfo>();
                    return Task.CompletedTask;
                });
                break;
        }

        await using var app = builder.Build();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("scoped service", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(PageInfo), exception.ToString(), StringComparison.Ordinal);
    }

    private StaticSiteBuilder CreateBuilder(string baseUrl)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root));
        builder.SiteInfo = new SiteInfo { BaseUrl = new Uri(baseUrl), Name = "Page info" };
        return builder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
