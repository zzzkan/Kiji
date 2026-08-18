using Kiji.Feeds;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="RssFeedArtifact"/>.
/// </summary>
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
            new FeedItem("Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero), "blog/hello/"),
        ]);

        var document = XDocument.Parse(await WriteFeedAsync(artifact, CreateContext()));

        var channel = document.Root!.Element("channel")!;
        Assert.Equal("Example Site", channel.Element("title")!.Value);
        Assert.Equal("https://example.com/", channel.Element("link")!.Value);
        Assert.Equal("An example site", channel.Element("description")!.Value);
        Assert.Equal("ja", channel.Element("language")!.Value);

        var item = Assert.Single(channel.Elements("item"));
        Assert.Equal("Hello World", item.Element("title")!.Value);
        Assert.Equal("https://example.com/blog/hello/", item.Element("link")!.Value);
        Assert.Equal("https://example.com/blog/hello/", item.Element("guid")!.Value);
        Assert.Equal("Wed, 18 Mar 2026 00:00:00 GMT", item.Element("pubDate")!.Value);
    }

    [Fact]
    public async Task WriteAsync_HalfHourOffset_ConvertsPubDateToUtc()
    {
        // 2026-03-18 09:30 +05:30 == 2026-03-18 04:00 UTC. The previous formatter
        // produced "+0500" for half-hour offsets, shifting the timestamp by 30 minutes.
        var artifact = new RssFeedArtifact(static _ =>
        [
            new FeedItem("India Post", "d", new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.FromMinutes(330)), "blog/india/"),
        ]);

        var document = XDocument.Parse(await WriteFeedAsync(artifact, CreateContext()));

        var item = Assert.Single(document.Root!.Element("channel")!.Elements("item"));
        Assert.Equal("Wed, 18 Mar 2026 04:00:00 GMT", item.Element("pubDate")!.Value);
    }

    [Fact]
    public async Task WriteAsync_EscapesXmlSpecialCharacters()
    {
        var artifact = new RssFeedArtifact(static _ =>
        [
            new FeedItem(
                """Ampersand & <angle> "quotes""",
                "Body with <tags> & entities",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "blog/tricky/"),
        ]);
        var context = CreateContext(
            siteName: "Tom & Jerry's <Site>",
            siteDescription: """A "quoted" description & more""");

        // Parsing back proves the output is well-formed despite the special characters.
        var document = XDocument.Parse(await WriteFeedAsync(artifact, context));
        var channel = document.Root!.Element("channel")!;
        Assert.Equal("Tom & Jerry's <Site>", channel.Element("title")!.Value);
        Assert.Equal("""A "quoted" description & more""", channel.Element("description")!.Value);

        var item = Assert.Single(channel.Elements("item"));
        Assert.Equal("""Ampersand & <angle> "quotes""", item.Element("title")!.Value);
        Assert.Equal("Body with <tags> & entities", item.Element("description")!.Value);
    }

    /// <summary>
    /// The feed is written in the order the factory yields, so a site orders entries
    /// however it wants them read — newest first, conventionally. Nothing re-sorts them.
    /// </summary>
    [Fact]
    public async Task WriteAsync_PreservesTheGivenOrder()
    {
        var artifact = new RssFeedArtifact(static _ =>
        [
            new FeedItem("Newest", "n", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), "blog/newest/"),
            new FeedItem("Middle", "m", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), "blog/middle/"),
            new FeedItem("Oldest", "o", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "blog/oldest/"),
        ]);

        var document = XDocument.Parse(await WriteFeedAsync(artifact, CreateContext()));

        var titles = document.Root!.Element("channel")!
            .Elements("item")
            .Select(static item => item.Element("title")!.Value)
            .ToArray();

        Assert.Equal(["Newest", "Middle", "Oldest"], titles);
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

    /// <summary>
    /// Feed URLs derive from <see cref="SiteInfo.BaseUrl"/>, so a site published under a
    /// sub-path needs no special handling — an item's RoutePath stays prefix-free.
    /// </summary>
    [Fact]
    public async Task WriteAsync_WithBasePath_IncludesThePrefixInChannelAndItemLinks()
    {
        var artifact = new RssFeedArtifact(static _ =>
        [
            new FeedItem("Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero), "blog/hello/"),
        ]);
        var context = CreateContext(baseUrl: "https://example.com/kiji/");

        var document = XDocument.Parse(await WriteFeedAsync(artifact, context));
        var channel = document.Root!.Element("channel")!;

        Assert.Equal("https://example.com/kiji/", channel.Element("link")!.Value);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        Assert.Equal("https://example.com/kiji/feed.xml", channel.Element(atom + "link")!.Attribute("href")!.Value);

        var item = Assert.Single(channel.Elements("item"));
        Assert.Equal("https://example.com/kiji/blog/hello/", item.Element("link")!.Value);
        Assert.Equal("https://example.com/kiji/blog/hello/", item.Element("guid")!.Value);
    }
}
