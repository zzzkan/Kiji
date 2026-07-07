using Kiji.Feeds;
using Kiji.Sitemaps;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticSiteGenerationRuntimeTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _outputDir;

    public StaticSiteGenerationRuntimeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"StaticSiteGenerationRuntimeTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "contents");
        _outputDir = Path.Combine(_testDir, "output");

        Directory.CreateDirectory(_contentsDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildSiteAsync_WritesDynamicBlogAndTagPagesAndFeed()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = GetStaticDirectory();
        builder.Paths.Output = _outputDir;

        IReadOnlyList<Post> items =
        [
            TestArticleContents.CreatePost(
                "hello-world",
                "Hello World",
                "Hello world description",
                new DateOnly(2026, 3, 18),
                new DateOnly(2026, 3, 19),
                "C# Basics",
                "Testing"),
            TestArticleContents.CreatePost(
                "other-post",
                "Other Post",
                "Other post description",
                new DateOnly(2026, 3, 17),
                null,
                "DotNet"),
        ];
        var posts = builder.AddContentSource<Post>(_ => items)
            .WithKey(static post => post.Slug)
            .OrderByDescending(static post => post.CreatedAt);

        await using var app = builder.Build();
        TestArticleContents.MapSite(app, posts);
        app.MapFeed(posts, static post => new FeedItem(post.Title, post.Description, post.CreatedAt));
        app.MapSitemap();

        await app.BuildSiteAsync();

        var blogHtml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "blog", "hello-world", "index.html"));
        Assert.Contains("<title>Hello World - zzzkan.me</title>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/blog/hello-world/\"", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<h1>Hello World</h1>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<p>Hello World</p>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("C# Basics", blogHtml, StringComparison.Ordinal);

        var tagHtml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "tags", "c-sharp-basics", "index.html"));
        Assert.Contains("<title>Tag: C# Basics - zzzkan.me</title>", tagHtml, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/tags/c-sharp-basics/\"", tagHtml, StringComparison.Ordinal);
        Assert.Contains("<h1>Tag: C# Basics</h1>", tagHtml, StringComparison.Ordinal);
        Assert.Contains("Hello World", tagHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Post", tagHtml, StringComparison.Ordinal);

        var notFoundHtml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "404.html"));
        Assert.NotEmpty(notFoundHtml);

        var sitemapXml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "sitemap.xml"));
        Assert.Contains("https://example.com/blog/hello-world/", sitemapXml, StringComparison.Ordinal);
        Assert.DoesNotContain("404.html", sitemapXml, StringComparison.Ordinal);

        var feedXml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "feed.xml"));
        Assert.Contains("<title>Hello World</title>", feedXml, StringComparison.Ordinal);
        Assert.Contains("https://example.com/blog/hello-world/", feedXml, StringComparison.Ordinal);
        // Feed entries follow collection order (newest first).
        Assert.True(
            feedXml.IndexOf("Hello World", StringComparison.Ordinal) < feedXml.IndexOf("Other Post", StringComparison.Ordinal));
    }

    private static string GetStaticDirectory()
    {
        return TestSitePaths.StaticDirectory;
    }
}
