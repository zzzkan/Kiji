using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="SitemapArtifact"/>.
/// </summary>
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

    private static async Task<XDocument> WriteSitemapAsync(SitemapArtifact artifact, SiteOutputContext context)
    {
        using var stream = new MemoryStream();
        await artifact.WriteAsync(stream, context, CancellationToken.None);
        return XDocument.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteAsync_ListsPagesSorted_AndExcludesOptedOutPages()
    {
        var context = CreateContext([
            new SitePageInfo("/blog/zebra/", "blog/zebra/index.html", false),
            new SitePageInfo("/", "index.html", false),
            new SitePageInfo("/404.html", "404.html", true),
            new SitePageInfo("/blog/alpha/", "blog/alpha/index.html", false),
        ]);
        var artifact = new SitemapArtifact();

        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = document.Root!
            .Elements(ns + "url")
            .Select(static url => url.Elements().First().Value)
            .ToArray();

        Assert.Equal(
            [
                "https://example.com/",
                "https://example.com/blog/alpha/",
                "https://example.com/blog/zebra/",
            ],
            locs);
        Assert.DoesNotContain("https://example.com/404.html", locs);
    }

    /// <summary>
    /// Sitemap URLs derive from <see cref="SiteInfo.BaseUrl"/>, so a site published under
    /// a sub-path needs no special handling. This pins that down.
    /// </summary>
    [Fact]
    public async Task WriteAsync_WithBasePath_IncludesThePrefixInEveryLocation()
    {
        var context = CreateContext(
            [
                new SitePageInfo("/", "index.html", false),
                new SitePageInfo("/blog/alpha/", "blog/alpha/index.html", false),
            ],
            "https://example.com/kiji/");
        var artifact = new SitemapArtifact();

        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = document.Root!
            .Elements(ns + "url")
            .Select(static url => url.Elements().First().Value)
            .ToArray();

        Assert.Equal(
            [
                "https://example.com/kiji/",
                "https://example.com/kiji/blog/alpha/",
            ],
            locs);
    }

    [Fact]
    public async Task WriteAsync_RouteWithSpecialCharacters_ProducesWellFormedXml()
    {
        var context = CreateContext([
            new SitePageInfo("/tags/c-and-cpp/?x=1&y=2", "tags/c-and-cpp/index.html", false),
        ]);
        var artifact = new SitemapArtifact();

        // Parsing back proves the output is well-formed despite the '&' in the route.
        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var loc = Assert.Single(document.Root!.Elements(ns + "url")).Elements().First().Value;
        Assert.Contains("x=1&y=2", loc, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sitemap carries locations only. <c>lastmod</c> was dropped along with the
    /// per-page timestamp that fed it.
    /// </summary>
    [Fact]
    public async Task WriteAsync_EmitsLocationsOnly()
    {
        var context = CreateContext([
            new SitePageInfo("/blog/hello/", "blog/hello/index.html", false),
        ]);
        var artifact = new SitemapArtifact();

        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var url = Assert.Single(document.Root!.Elements(ns + "url"));
        Assert.Equal("https://example.com/blog/hello/", url.Element(ns + "loc")!.Value);
        Assert.Null(url.Element(ns + "lastmod"));
    }

    [Fact]
    public void Constructor_CustomPath_IsExposed()
    {
        var artifact = new SitemapArtifact("seo/sitemap.xml");

        Assert.Equal("seo/sitemap.xml", artifact.OutputRelativePath);
    }
}
