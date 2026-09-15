using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Tests for <see cref="ISiteArtifact"/> registration via <see cref="StaticSite.AddArtifact"/>
/// and <see cref="SiteOutputContext"/>.
/// </summary>
public sealed class SiteArtifactTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _outputDir;

    public SiteArtifactTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SiteArtifactTests_{Guid.NewGuid():N}");
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

    private sealed class RecordingArtifact(string outputRelativePath) : ISiteArtifact
    {
        public string OutputRelativePath { get; } = outputRelativePath;

        public SiteOutputContext? ObservedContext { get; private set; }

        public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
        {
            ObservedContext = context;
            await output.WriteAsync(Encoding.UTF8.GetBytes($"artifact from {context.Site.Name}"), cancellationToken);
        }
    }

    [Fact]
    public async Task PublishAsync_WritesArtifact_AndExposesPagesAndRouteResolution()
    {
        var artifact = new RecordingArtifact("meta/info.txt");
        await using var app = await CreateAppAsync(artifact);
        app.AddPages<MirrorPostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        app.AddArtifact(new Kiji.Sitemaps.SitemapArtifact("seo/sitemap.xml"));

        await app.PublishAsync(_outputDir);

        var written = await File.ReadAllTextAsync(Path.Combine(_outputDir, "meta", "info.txt"));
        Assert.Equal("artifact from zzzkan.me", written);

        Assert.True(File.Exists(Path.Combine(_outputDir, "mirror", "hello-world", "index.html")));
        Assert.Contains("/mirror/hello-world/", await File.ReadAllTextAsync(Path.Combine(_outputDir, "seo", "sitemap.xml")), StringComparison.Ordinal);
        var context = artifact.ObservedContext!;
        Assert.Contains(context.Pages, static page => page.RoutePath == "/blog/hello-world/");
        Assert.Contains(context.Pages, static page => page is { RoutePath: "/404.html", ExcludeFromSitemap: true });

    }

    [Fact]
    public async Task PublishAsync_ArtifactPathEscapingOutputDirectory_Throws()
    {
        var artifact = new RecordingArtifact(Path.Combine("..", "evil.txt"));
        await using var app = await CreateAppAsync(artifact);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(_outputDir));

        Assert.Contains("escapes the output directory", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_testDir, "evil.txt")));
    }

    [Fact]
    public async Task PublishAsync_ArtifactPathCollidingWithGeneratedPage_Throws()
    {
        var artifact = new RecordingArtifact(Path.Combine("blog", "hello-world", "index.html"));
        await using var app = await CreateAppAsync(artifact);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(_outputDir));

        Assert.Contains("collides with a generated page output path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_ArtifactPathCollidingWithStaticFile_Throws()
    {
        var staticDir = Path.Combine(_testDir, "static");
        Directory.CreateDirectory(staticDir);
        await File.WriteAllTextAsync(Path.Combine(staticDir, "feed.xml"), "static");

        var artifact = new RecordingArtifact("feed.xml");
        await using var app = await CreateAppAsync([artifact], staticDir);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(_outputDir));

        Assert.Contains("collides with a static file output path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_DuplicateArtifactPath_Throws()
    {
        var first = new RecordingArtifact(Path.Combine("meta", "info.txt"));
        var second = new RecordingArtifact(Path.Combine("meta", "info.txt"));
        await using var app = await CreateAppAsync([first, second], staticPath: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(_outputDir));

        Assert.Contains("collides with another artifact output path", exception.Message, StringComparison.Ordinal);
    }

    private async Task<StaticSite> CreateAppAsync(ISiteArtifact artifact)
    {
        return await CreateAppAsync([artifact], staticPath: null);
    }

    private async Task<StaticSite> CreateAppAsync(IReadOnlyList<ISiteArtifact> artifacts, string? staticPath)
    {
        return await CreateAppCoreAsync(artifacts, staticPath);
    }

    private async Task<StaticSite> CreateAppCoreAsync(
        IReadOnlyList<ISiteArtifact> artifacts,
        string? staticPath)
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _testDir;
        app.Paths.ContentDirectory = _contentsDir;
        app.Paths.StaticDirectory = staticPath ?? TestSitePaths.StaticDirectory;

        IReadOnlyList<Post> items =
        [
            TestArticleContents.CreatePost(
                "hello-world",
                "Hello World",
                "Hello world description",
                new DateOnly(2026, 3, 18),
                null,
                "Testing"),
        ];
        app.UseContentSource<Post>(_ => items, static post => post.Slug);
        TestArticleContents.MapSite(app);
        foreach (var artifact in artifacts)
        {
            app.AddArtifact(artifact);
        }

        await Task.CompletedTask;
        return app;
    }
}
