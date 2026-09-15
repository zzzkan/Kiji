using Kiji.Feeds;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using Xunit;

namespace Kiji.Tests;

public sealed class FeedsTests
{
    private static SiteOutputContext CreateContext(
        string siteName = "Example Site",
        string siteDescription = "An example site",
        string baseUrl = "https://example.com")
    {
        var site = new SiteInfo
        {
            BaseUrl = new Uri(baseUrl),
            Name = siteName,
            Description = siteDescription,
            Language = "ja",
        };

        return new SiteOutputContext(site, [], new ServiceCollection().BuildServiceProvider());
    }

    private static async Task<string> WriteFeedAsync(RssFeedArtifact artifact, SiteOutputContext context)
    {
        using var stream = new MemoryStream();
        await artifact.WriteAsync(stream, context, CancellationToken.None);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ProducesWellFormedRssWithChannelMetadata()
    {
        var artifact = new RssFeedArtifact(static _ =>
        [
            new FeedItem("Hello & <World>", "First <post> & more", new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.FromMinutes(330)), "blog/hello/"),
            new FeedItem("Later", "Second", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), "blog/later/"),
        ]);

        var document = XDocument.Parse(await WriteFeedAsync(artifact, CreateContext(siteName: "Example & <Site>", baseUrl: "https://example.com/kiji/")));

        var channel = document.Root!.Element("channel")!;
        Assert.Equal("Example & <Site>", channel.Element("title")!.Value);
        Assert.Equal("https://example.com/kiji/", channel.Element("link")!.Value);
        Assert.Equal("An example site", channel.Element("description")!.Value);
        Assert.Equal("ja", channel.Element("language")!.Value);

        var items = channel.Elements("item").ToArray();
        Assert.Equal(["Hello & <World>", "Later"], items.Select(item => item.Element("title")!.Value));
        var item = items[0];
        Assert.Equal("First <post> & more", item.Element("description")!.Value);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        Assert.Equal("https://example.com/kiji/feed.xml", channel.Element(atom + "link")!.Attribute("href")!.Value);
        Assert.Equal("Hello & <World>", item.Element("title")!.Value);
        Assert.Equal("https://example.com/kiji/blog/hello/", item.Element("link")!.Value);
        Assert.Equal("https://example.com/kiji/blog/hello/", item.Element("guid")!.Value);
        Assert.Equal("Wed, 18 Mar 2026 04:00:00 GMT", item.Element("pubDate")!.Value);
    }

    [Fact]
    public async Task WriteAsync_CustomPath_SetsAtomSelfLink()
    {
        var artifact = new RssFeedArtifact(static _ => [], outputRelativePath: "rss/all.xml");

        Assert.Equal("rss/all.xml", artifact.OutputRelativePath);

        var document = XDocument.Parse(await WriteFeedAsync(artifact, CreateContext()));

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var selfLink = document.Root!.Element("channel")!.Element(atom + "link")!;
        Assert.Equal("https://example.com/rss/all.xml", selfLink.Attribute("href")!.Value);
    }

}
