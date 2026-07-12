using System.Xml.Linq;
using Kiji.Sitemaps;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="SitemapArtifact"/>.
/// </summary>
public sealed class SitemapsTests
{
    private static SiteOutputContext CreateContext(IReadOnlyList<SitePageInfo> pages)
    {
        var site = new SiteInfo
        {
            BaseUrl = new Uri("https://example.com"),
            Name = "Example Site",
        };

        return new SiteOutputContext(site, pages);
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
            new SitePageInfo("/blog/zebra/", "blog/zebra/index.html", false, null),
            new SitePageInfo("/", "index.html", false, null),
            new SitePageInfo("/404.html", "404.html", true, null),
            new SitePageInfo("/blog/alpha/", "blog/alpha/index.html", false, null),
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

    [Fact]
    public async Task WriteAsync_RouteWithSpecialCharacters_ProducesWellFormedXml()
    {
        var context = CreateContext([
            new SitePageInfo("/tags/c-and-cpp/?x=1&y=2", "tags/c-and-cpp/index.html", false, null),
        ]);
        var artifact = new SitemapArtifact();

        // Parsing back proves the output is well-formed despite the '&' in the route.
        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var loc = Assert.Single(document.Root!.Elements(ns + "url")).Elements().First().Value;
        Assert.Contains("x=1&y=2", loc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_PageWithLastModified_EmitsLastmodInUtc()
    {
        var context = CreateContext([
            new SitePageInfo(
                "/blog/dated/",
                "blog/dated/index.html",
                false,
                null,
                new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.FromMinutes(330))),
            new SitePageInfo("/blog/undated/", "blog/undated/index.html", false, null),
        ]);
        var artifact = new SitemapArtifact();

        var document = await WriteSitemapAsync(artifact, context);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = document.Root!.Elements(ns + "url").ToArray();
        var dated = Assert.Single(urls, url => url.Element(ns + "loc")!.Value.Contains("/dated/", StringComparison.Ordinal));
        var undated = Assert.Single(urls, url => url.Element(ns + "loc")!.Value.Contains("/undated/", StringComparison.Ordinal));

        Assert.Equal("2026-03-18T04:00:00Z", dated.Element(ns + "lastmod")!.Value);
        Assert.Null(undated.Element(ns + "lastmod"));
    }

    [Fact]
    public void Constructor_CustomPath_IsExposed()
    {
        var artifact = new SitemapArtifact("seo/sitemap.xml");

        Assert.Equal("seo/sitemap.xml", artifact.OutputRelativePath);
    }
}
