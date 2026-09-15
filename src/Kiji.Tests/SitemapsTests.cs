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
            new SitePageInfo("/blog/zebra/?x=1&y=2", "blog/zebra/index.html", false),
            new SitePageInfo("/", "index.html", false),
            new SitePageInfo("/404.html", "404.html", true),
            new SitePageInfo("/blog/alpha/", "blog/alpha/index.html", false),
        ], "https://example.com/kiji/");
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
                "https://example.com/kiji/blog/zebra/?x=1&y=2",
            ],
            locs);
        Assert.DoesNotContain("https://example.com/kiji/404.html", locs);
    }

}
