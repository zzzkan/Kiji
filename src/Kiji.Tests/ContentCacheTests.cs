using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class ContentCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiji-content-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CustomDataDigests_InvalidateOnlyReaders_AndExternalValuesAreTracked()
    {
        var first = "first";
        var second = "second";
        var prefix = "prefix:";
        var firstRenders = 0;
        var secondRenders = 0;
        await using var site = CreateSite(() =>
        [
            new("one", new(first, () => Interlocked.Increment(ref firstRenders)), first),
            new("two", new(second, () => Interlocked.Increment(ref secondRenders)), second),
        ], () => prefix);
        await site.PublishAsync("dist");
        await site.PublishAsync("dist");
        Assert.Equal(1, firstRenders);
        Assert.Equal(1, secondRenders);
        first = "changed";
        await site.PublishAsync("dist");
        Assert.Equal(2, firstRenders);
        Assert.Equal(1, secondRenders);
        prefix = "new:";
        await site.PublishAsync("dist");
        Assert.Equal(3, firstRenders);
        Assert.Equal(2, secondRenders);
        Assert.Contains("new:changed", await File.ReadAllTextAsync(Path.Combine(_root, "dist", "cache", "one", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDigest_DisablesOnlyItsReaders()
    {
        var uncached = 0;
        var cached = 0;
        await using var site = CreateSite(() =>
        [
            new("one", new("first", () => Interlocked.Increment(ref uncached)), null),
            new("two", new("second", () => Interlocked.Increment(ref cached)), "stable"),
        ], () => "");
        await site.PublishAsync("dist");
        await site.PublishAsync("dist");
        Assert.Equal(2, uncached);
        Assert.Equal(1, cached);
        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(_root, ".kiji", "cache", "manifest.json")),
            Generation.BuildManifestJsonContext.Default.BuildManifest)!;
        Assert.Equal(0, manifest.Pages.Single(page => page.RoutePath == "/cache/one/").HtmlLength);
        Assert.True(manifest.Pages.Single(page => page.RoutePath == "/cache/two/").HtmlLength > 0);
    }

    private StaticSite CreateSite(Func<IReadOnlyList<ContentEntry<CacheItem>>> load, Func<string> prefix)
    {
        Directory.CreateDirectory(_root);
        var site = StaticSite.Create([]);
        site.Paths.RootDirectory = _root;
        site.Info = TestArticleContents.CreateSiteInfo();
        site.UseContentSource<CacheItem>("data", _ => load());
        site.AddPageInput("prefix", prefix);
        site.AddPages<CacheItemPage>(services => services.GetRequiredService<ContentDictionary<CacheItem>>()
            .Select(entry => new { Id = entry.Key }));
        return site;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
