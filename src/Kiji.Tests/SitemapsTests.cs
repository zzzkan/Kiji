using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using Xunit;

namespace Kiji.Tests;

public sealed class SitemapsTests
{
    private static SiteOutputContext CreateContext(IReadOnlyList<SitePageInfo> pages, string baseUrl = "https://example.com")
    {
        var site = new SiteInfo
        {
            BaseUrl = new Uri(baseUrl),
            Name = "Example Site",
        };

        return new SiteOutputContext(site, pages, new ServiceCollection().BuildServiceProvider());
    }

    private static async Task<XDocument> WriteSitemapAsync(
        SiteOutputContext context,
        IEnumerable<string>? excludedPaths = null)
    {
        using var stream = new MemoryStream();
        var artifact = new SitemapArtifact(excludedPaths: excludedPaths);
        await artifact.WriteAsync(stream, context, CancellationToken.None);
        return XDocument.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteAsync_ListsPagesSorted_AndExcludesNotFoundByDefault()
    {
        var context = CreateContext([
            new SitePageInfo("blog/zebra/", "blog/zebra/index.html"),
            new SitePageInfo("", "index.html"),
            new SitePageInfo("404.html", "404.html"),
            new SitePageInfo("blog/alpha/", "blog/alpha/index.html"),
        ], "https://example.com/kiji/");

        var document = await WriteSitemapAsync(context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = document.Root!
            .Elements(ns + "url")
            .Select(static url => url.Elements().First().Value)
            .ToArray();

        Assert.Equal(
            [
                "https://example.com/kiji/",
                "https://example.com/kiji/blog/alpha/",
                "https://example.com/kiji/blog/zebra/",
            ],
            locs);
    }

    [Fact]
    public async Task WriteAsync_ExcludesConfiguredPaths()
    {
        var context = CreateContext([
            new SitePageInfo("", "index.html"),
            new SitePageInfo("preview/", "preview/index.html"),
            new SitePageInfo("public/", "public/index.html"),
        ]);

        var document = await WriteSitemapAsync(context, ["preview/"]);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = document.Root!
            .Elements(ns + "url")
            .Select(static url => url.Elements().First().Value)
            .ToArray();

        Assert.Equal(["https://example.com/", "https://example.com/public/"], locs);
    }

    [Fact]
    public async Task WriteAsync_ExcludesPagesWithEncodedRouteValues_InEitherSpelling()
    {
        // Route parameter values reach the page path percent-encoded; literal segments do not.
        var context = CreateContext([
            new SitePageInfo("posts/%E6%97%A5%E6%9C%AC%E8%AA%9E/", "posts/日本語/index.html"),
            new SitePageInfo("posts/my%20post/", "posts/my post/index.html"),
            new SitePageInfo("下書き/", "下書き/index.html"),
            new SitePageInfo("posts/a%2Fb/", "posts/a/b/index.html"),
            new SitePageInfo("posts/a/b/", "posts/a/b/index.html"),
        ], "https://example.com/kiji/");

        var document = await WriteSitemapAsync(context,
            ["posts/日本語/", "posts/my%20post/", "%E4%B8%8B%E6%9B%B8%E3%81%8D/", "posts/a/b/"]);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var loc = Assert.Single(document.Root!.Elements(ns + "url")).Elements().First().Value;
        Assert.Equal("https://example.com/kiji/posts/a%2Fb/", loc);
    }

    [Theory]
    [InlineData("/preview/")]
    [InlineData("preview/../internal/")]
    [InlineData(" preview/")]
    public void Constructor_RejectsInvalidExcludedPath(string path)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SitemapArtifact(excludedPaths: [path]));
        Assert.Equal("excludedPaths", exception.ParamName);
    }

}
