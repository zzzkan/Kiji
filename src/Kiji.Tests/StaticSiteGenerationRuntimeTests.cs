using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.Tests.TestSite;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
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
        _contentsDir = Path.Combine(_testDir, "content");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishAsync_NavigationFailsWithoutPublishingPage(bool refresh)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir));
        builder.Info = TestArticleContents.CreateSiteInfoWithBasePath();
        void Navigate(NavigationManager navigation)
        {
            if (refresh)
            {
                navigation.Refresh();
            }
            else
            {
                navigation.NavigateTo("target/");
            }
        }
        builder.AddPageParameters<NavigationTestPage>(_ => [new { Id = "original", Navigate = (Action<NavigationManager>)Navigate }]);

        await using var app = builder.Build();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => app.PublishAsync(_outputDir));

        Assert.Contains("NavigationManager", exception.Message, StringComparison.Ordinal);
        Assert.Contains("static rendering", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_outputDir, "navigation", "original", "index.html")));
    }

    [Fact]
    public async Task PublishAsync_WritesDynamicBlogAndTagPagesAndFeed()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")));
        builder.Info = TestArticleContents.CreateSiteInfo();
        TestSiteAssets.Bind(builder.Paths, GetStaticDirectory());

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
        builder.AddContentSource(_ => items);
        TestArticleContents.MapSite(builder);

        builder.AddRssFeed(static services => services.GetRequiredService<ContentDictionary<Post>>().Values
            .OrderByDescending(static post => post.CreatedAt)
            .Select(static post => new FeedItem(
                post.Title, post.Description, post.CreatedAt, RelativePath: $"blog/{post.Slug}/")));
        builder.AddSitemap();

        await using var app = builder.Build();
        await app.PublishAsync(_outputDir);

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

        var feedXml = await File.ReadAllTextAsync(Path.Combine(_outputDir, "feed.xml"));
        Assert.Contains("<title>Hello World</title>", feedXml, StringComparison.Ordinal);
        Assert.Contains("https://example.com/blog/hello-world/", feedXml, StringComparison.Ordinal);
        // Feed entries appear in the order AddRssFeed produced them (newest first).
        Assert.True(
            feedXml.IndexOf("Hello World", StringComparison.Ordinal) < feedXml.IndexOf("Other Post", StringComparison.Ordinal));

        var manifest = await ReadManifestAsync();
        var detail = manifest.Pages.Single(page => page.OutputRelativePath == Path.Combine("blog", "hello-world", "index.html"));
        Assert.Null(detail.ParametersHash); // This fixture has no data digest, so its readers cannot be reused.
    }

    [Fact]
    public async Task PublishAsync_MarkdownImages_BundledWithPageAndRootRelativeUrls()
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

        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")));
        builder.Info = TestArticleContents.CreateSiteInfoWithBasePath();
        TestSiteAssets.Bind(builder.Paths, GetStaticDirectory());

        builder.AddMarkdownContent<FrontMatter>();
        builder.AddContentSource<Post>(static _ => []);
        TestArticleContents.MapSite(builder);
        builder.AddPageParameters<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));

        await using var app = builder.Build();
        await app.PublishAsync(_outputDir);

        var pageDir = Path.Combine(_outputDir, "md", "hello-world");
        var html = await File.ReadAllTextAsync(Path.Combine(pageDir, "index.html"));

        Assert.DoesNotContain("<base", html, StringComparison.OrdinalIgnoreCase);
        var home = await File.ReadAllTextAsync(Path.Combine(_outputDir, "index.html"));
        Assert.Contains("href=\"/kiji/css/app.css\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/kiji/\"", home, StringComparison.Ordinal);

        // Page-bundle layout: variants stay beside index.html and their URLs include the deployment base path.
        Assert.Contains("src=\"/kiji/md/hello-world/photo.png.", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/kiji/md/hello-world/images/nested.png.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/_assets/", html, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(pageDir, "photo.png.*.webp"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(pageDir, "images"), "nested.png.*.webp"));

        // Encoded variants are kept in the persistent cache under .kiji/cache.
        var imageCacheDir = Path.Combine(_testDir, ".kiji", "cache", "images");
        Assert.NotEmpty(Directory.GetFiles(imageCacheDir, "*", SearchOption.AllDirectories));

        var manifest = await ReadManifestAsync();
        var detail = manifest.Pages.Single(page => page.OutputRelativePath == Path.Combine("md", "hello-world", "index.html"));
        Assert.Contains(detail.Dependencies, static dependency => dependency is { Kind: "file", Key: "content/hello/hello-world.md" });
    }

    [Fact]
    public async Task PublishAsync_ChangedImageSettings_ReplaceReusedVariants()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "photos.md"), "---\ntitle: Photos\n---\n\n![Photo](photo.png)\n");
        await CreateTestImageAsync(Path.Combine(_contentsDir, "photo.png"), 1400, 700);
        var pageDir = Path.Combine(_outputDir, "md", "photos");

        async Task<string> PublishAsync(Action<Kiji.Assets.ImageProcessorOptions>? configure = null)
        {
            var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir));
            builder.Info = TestArticleContents.CreateSiteInfo();
            if (configure is not null) { builder.Services.Configure(configure); }
            builder.AddMarkdownContent<FrontMatter>();
            builder.AddPageParameters<MarkdownPostTestPage>(static services => services
                .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
                .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));
            await using var app = builder.Build();
            await app.PublishAsync(_outputDir);
            return await File.ReadAllTextAsync(Path.Combine(pageDir, "index.html"));
        }

        var original = await PublishAsync();
        Assert.Contains(" 1280w", original, StringComparison.Ordinal);
        Assert.Equal(5, Directory.GetFiles(pageDir, "photo.png.*.webp").Length);

        var configured = await PublishAsync(static options => { options.Widths = [480]; options.MaxWidth = 900; });
        Assert.Contains(" 480w, ", configured, StringComparison.Ordinal);
        Assert.Contains(" 900w\"", configured, StringComparison.Ordinal);
        Assert.DoesNotContain(" 1280w", configured, StringComparison.Ordinal);
        Assert.Equal(["480w", "900w"], Directory.GetFiles(pageDir, "photo.png.*.webp")
            .Select(static file => Path.GetFileNameWithoutExtension(file).Split('.')[^1]).Order(StringComparer.Ordinal));
    }

    private async Task<Kiji.Generation.BuildManifest> ReadManifestAsync()
    {
        return System.Text.Json.JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(Path.Combine(_testDir, ".kiji", "cache", "manifest.json")),
            Kiji.Generation.BuildManifestJsonContext.Default.BuildManifest)!;
    }

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, new SKColor((byte)(x % 255), (byte)(y % 255), 128));
            }
        }

        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, encoded.ToArray());
    }

    private static string GetStaticDirectory()
    {
        return TestSitePaths.StaticDirectory;
    }
}
