using System.Xml.Linq;
using Kiji.Feeds;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="RssFeedArtifact{TContent}"/>.
/// </summary>
public sealed class FeedsTests
{
    private sealed record Entry(string Slug, string Title, string Description, DateTimeOffset PublishedAt);

    private static SiteOutputContext CreateContext(
        IReadOnlyList<SitePageInfo> pages,
        string siteName = "Example Site",
        string siteDescription = "An example site")
    {
        var site = new SiteInfo
        {
            BaseUrl = new Uri("https://example.com"),
            Name = siteName,
            Description = siteDescription,
            Language = "ja",
        };

        return new SiteOutputContext(site, pages);
    }

    private static async Task<string> WriteFeedAsync<TContent>(RssFeedArtifact<TContent> artifact, SiteOutputContext context)
        where TContent : class
    {
        using var stream = new MemoryStream();
        await artifact.WriteAsync(stream, context, CancellationToken.None);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ProducesWellFormedRssWithChannelMetadata()
    {
        var entries = Content.FromItems<Entry>([
            new("hello", "Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([new SitePageInfo("/blog/hello/", "blog/hello/index.html", false, "hello")]);
        var artifact = new RssFeedArtifact<Entry>(entries, static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt));

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

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
        var entries = Content.FromItems<Entry>([
            new("india", "India Post", "d", new DateTimeOffset(2026, 3, 18, 9, 30, 0, TimeSpan.FromMinutes(330))),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([new SitePageInfo("/blog/india/", "blog/india/index.html", false, "india")]);
        var artifact = new RssFeedArtifact<Entry>(entries, static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt));

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

        var item = Assert.Single(document.Root!.Element("channel")!.Elements("item"));
        Assert.Equal("Wed, 18 Mar 2026 04:00:00 GMT", item.Element("pubDate")!.Value);
    }

    [Fact]
    public async Task WriteAsync_ContentHtml_EmitsContentEncoded()
    {
        var entries = Content.FromItems<Entry>([
            new("hello", "Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([new SitePageInfo("/blog/hello/", "blog/hello/index.html", false, "hello")]);
        var artifact = new RssFeedArtifact<Entry>(
            entries,
            static (entry, _) => Task.FromResult(new FeedItem(entry.Title, entry.Description, entry.PublishedAt)
            {
                ContentHtml = "<p>Full <strong>HTML</strong> body.</p>",
            }));

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

        XNamespace content = "http://purl.org/rss/1.0/modules/content/";
        var item = Assert.Single(document.Root!.Element("channel")!.Elements("item"));
        Assert.Equal("<p>Full <strong>HTML</strong> body.</p>", item.Element(content + "encoded")!.Value);
    }

    [Fact]
    public async Task WriteAsync_ItemSelector_RunsUnderItemPageRenderContext()
    {
        var entries = Content.FromItems<Entry>([
            new("hello", "Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([new SitePageInfo("/blog/hello/", Path.Combine("blog", "hello", "index.html"), false, "hello")]);
        var artifact = new RssFeedArtifact<Entry>(entries, static (entry, _) =>
        {
            // Content renderers (e.g. MarkdownContent.RenderAsync) rely on this ambient
            // context to hit the per-page cache built during page generation.
            var renderContext = Kiji.Rendering.PageRenderContext.Current;
            Assert.NotNull(renderContext);
            Assert.Equal("/blog/hello/", renderContext.RoutePath);
            Assert.Equal(Path.Combine("blog", "hello"), renderContext.OutputRelativeDirectory);
            return Task.FromResult(new FeedItem(entry.Title, entry.Description, entry.PublishedAt));
        });

        var xml = await WriteFeedAsync(artifact, context);

        Assert.Contains("Hello World", xml, StringComparison.Ordinal);
        Assert.Null(Kiji.Rendering.PageRenderContext.Current);
    }

    [Fact]
    public async Task WriteAsync_WithoutContentHtml_OmitsContentEncoded()
    {
        var entries = Content.FromItems<Entry>([
            new("hello", "Hello World", "First post", new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([new SitePageInfo("/blog/hello/", "blog/hello/index.html", false, "hello")]);
        var artifact = new RssFeedArtifact<Entry>(entries, static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt));

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

        XNamespace content = "http://purl.org/rss/1.0/modules/content/";
        var item = Assert.Single(document.Root!.Element("channel")!.Elements("item"));
        Assert.Null(item.Element(content + "encoded"));
    }

    [Fact]
    public async Task WriteAsync_EscapesXmlSpecialCharacters()
    {
        var entries = Content.FromItems<Entry>([
            new("tricky", """Ampersand & <angle> "quotes""", "Body with <tags> & entities", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext(
            [new SitePageInfo("/blog/tricky/", "blog/tricky/index.html", false, "tricky")],
            siteName: "Tom & Jerry's <Site>",
            siteDescription: """A "quoted" description & more""");
        var artifact = new RssFeedArtifact<Entry>(entries, static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt));

        var xml = await WriteFeedAsync(artifact, context);

        // Parsing back proves the output is well-formed despite the special characters.
        var document = XDocument.Parse(xml);
        var channel = document.Root!.Element("channel")!;
        Assert.Equal("Tom & Jerry's <Site>", channel.Element("title")!.Value);
        Assert.Equal("""A "quoted" description & more""", channel.Element("description")!.Value);

        var item = Assert.Single(channel.Elements("item"));
        Assert.Equal("""Ampersand & <angle> "quotes""", item.Element("title")!.Value);
        Assert.Equal("Body with <tags> & entities", item.Element("description")!.Value);
    }

    [Fact]
    public async Task WriteAsync_PreservesCollectionOrder_AndSkipsUnmappedItems()
    {
        var entries = Content.FromItems<Entry>([
            new("newest", "Newest", "n", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            new("unmapped", "Unmapped", "u", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new("oldest", "Oldest", "o", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        ]).WithKey(static entry => entry.Slug);
        var context = CreateContext([
            new SitePageInfo("/blog/newest/", "blog/newest/index.html", false, "newest"),
            new SitePageInfo("/blog/oldest/", "blog/oldest/index.html", false, "oldest"),
        ]);
        var artifact = new RssFeedArtifact<Entry>(entries, static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt));

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

        var titles = document.Root!.Element("channel")!
            .Elements("item")
            .Select(static item => item.Element("title")!.Value)
            .ToArray();

        Assert.Equal(["Newest", "Oldest"], titles);
    }

    [Fact]
    public async Task WriteAsync_CustomPath_SetsAtomSelfLink()
    {
        var entries = Content.FromItems<Entry>([]).WithKey(static entry => entry.Slug);
        var context = CreateContext([]);
        var artifact = new RssFeedArtifact<Entry>(
            entries,
            static entry => new FeedItem(entry.Title, entry.Description, entry.PublishedAt),
            outputRelativePath: "rss/all.xml");

        Assert.Equal("rss/all.xml", artifact.OutputRelativePath);

        var xml = await WriteFeedAsync(artifact, context);
        var document = XDocument.Parse(xml);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var selfLink = document.Root!.Element("channel")!.Element(atom + "link")!;
        Assert.Equal("https://example.com/rss/all.xml", selfLink.Attribute("href")!.Value);
    }
}
