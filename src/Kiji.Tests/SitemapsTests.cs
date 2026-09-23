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
    public void Constructor_RejectsInvalidExcludedPath()
    {
        Assert.Throws<ArgumentException>(() => new SitemapArtifact(excludedPaths: ["/preview/"]));
    }

}
