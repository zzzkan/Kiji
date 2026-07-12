using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.Tests.TestSite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
                "C Sharp Basics",
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
        Assert.StartsWith("<!doctype html>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"ja\">", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\"", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<title>Hello World - zzzkan.me</title>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/blog/hello-world/\"", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<h1>Hello World</h1>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("<p>Hello World</p>", blogHtml, StringComparison.Ordinal);
        Assert.Contains("C Sharp Basics", blogHtml, StringComparison.Ordinal);

        var tagHtml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "tags", "c-sharp-basics", "index.html"));
        Assert.Contains("<title>Tag: C Sharp Basics - zzzkan.me</title>", tagHtml, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/tags/c-sharp-basics/\"", tagHtml, StringComparison.Ordinal);
        Assert.Contains("<h1>Tag: C Sharp Basics</h1>", tagHtml, StringComparison.Ordinal);
        Assert.Contains("Hello World", tagHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Post", tagHtml, StringComparison.Ordinal);

        var notFoundHtml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "404.html"));
        Assert.NotEmpty(notFoundHtml);

        var sitemapXml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "sitemap.xml"));
        Assert.Contains("https://example.com/blog/hello-world/", sitemapXml, StringComparison.Ordinal);
        Assert.DoesNotContain("404.html", sitemapXml, StringComparison.Ordinal);
        // lastmod flows from the MapContent lastModified selector (UpdatedAt of hello-world).
        Assert.Contains("<lastmod>2026-03-19T00:00:00Z</lastmod>", sitemapXml, StringComparison.Ordinal);

        var feedXml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "feed.xml"));
        Assert.Contains("<title>Hello World</title>", feedXml, StringComparison.Ordinal);
        Assert.Contains("https://example.com/blog/hello-world/", feedXml, StringComparison.Ordinal);
        // Feed entries follow collection order (newest first).
        Assert.True(
            feedXml.IndexOf("Hello World", StringComparison.Ordinal) < feedXml.IndexOf("Other Post", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildSiteAsync_MarkdownImages_BundledWithPageAndRelativelyReferenced()
    {
        var postDir = Path.Combine(_contentsDir, "hello");
        Directory.CreateDirectory(Path.Combine(postDir, "images"));
        await File.WriteAllTextAsync(
            Path.Combine(postDir, "hello-world.md"),
            """
            ---
            title: Hello World
            createdAt: 2026-03-18
            ---

            ![Beside](photo.png)

            ![Nested](./images/nested.png)
            """);
        await CreateTestImageAsync(Path.Combine(postDir, "photo.png"), 800, 600);
        await CreateTestImageAsync(Path.Combine(postDir, "images", "nested.png"), 400, 300);

        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = GetStaticDirectory();
        builder.Paths.Output = _outputDir;

        var markdownPosts = builder.AddMarkdownContent<FrontMatter>()
            .WithKey(static post => post.FileInfo.FileNameWithoutExtension);
        var posts = builder.AddContentSource<Post>(static _ => [])
            .WithKey(static post => post.Slug);

        await using var app = builder.Build();
        TestArticleContents.MapSite(app, posts);
        app.MapContent<MarkdownPostTestPage, MarkdownContent<FrontMatter>>(
            markdownPosts,
            static post => new { Slug = post.FileInfo.FileNameWithoutExtension });

        await app.BuildSiteAsync();

        var pageDir = Path.Combine(_outputDir, "md", "hello-world");
        var html = await File.ReadAllTextAsync(Path.Combine(pageDir, "index.html"));

        // Page-bundle layout: variants beside index.html, referenced with ./ URLs.
        Assert.Contains("src=\"./photo.png.", html, StringComparison.Ordinal);
        Assert.Contains("src=\"./images/nested.png.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/_assets/", html, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(pageDir, "photo.png.*.webp"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(pageDir, "images"), "nested.png.*.webp"));

        // Encoded variants are kept in the persistent cache under .kiji/cache.
        var imageCacheDir = Path.Combine(_testDir, ".kiji", "cache", "images");
        Assert.NotEmpty(Directory.GetFiles(imageCacheDir, "*.webp", SearchOption.AllDirectories));
    }

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32((byte)(x % 255), (byte)(y % 255), 128);
            }
        }

        await image.SaveAsPngAsync(path);
    }

    private static string GetStaticDirectory()
    {
        return TestSitePaths.StaticDirectory;
    }
}
