using Kiji.Tests.TestSite;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Tests for delegate artifact registration and <see cref="SiteOutputContext"/>.
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

    private sealed class RecordingArtifact(string outputRelativePath)
    {
        public string OutputRelativePath { get; } = outputRelativePath;

        public SiteOutputContext? ObservedContext { get; private set; }

        public void Register(StaticSite app) => app.AddArtifact(OutputRelativePath, WriteAsync);

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
        app.AddSitemap("seo/sitemap.xml", ["mirror/hello-world/"]);

        await app.PublishAsync(_outputDir);

        var written = await File.ReadAllTextAsync(Path.Combine(_outputDir, "meta", "info.txt"));
        Assert.Equal("artifact from zzzkan.me", written);

        Assert.True(File.Exists(Path.Combine(_outputDir, "mirror", "hello-world", "index.html")));
        var sitemap = await File.ReadAllTextAsync(Path.Combine(_outputDir, "seo", "sitemap.xml"));
        Assert.DoesNotContain("/mirror/hello-world/", sitemap, StringComparison.Ordinal);
        Assert.Contains("/blog/hello-world/", sitemap, StringComparison.Ordinal);
        var context = artifact.ObservedContext!;
        Assert.Contains(context.Pages, static page => page.RelativePath == "blog/hello-world/");
        Assert.Contains(context.Pages, static page => page.RelativePath == "404.html");
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

    [Fact]
    public async Task PublishAsync_PassesCancellationToArtifactWriter()
    {
        await using var app = await CreateAppAsync([],
            staticPath: null);
        using var cancellation = new CancellationTokenSource();
        var observed = false;
        app.AddArtifact("cancel.txt", (_, _, token) =>
        {
            observed = true;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => app.PublishAsync(_outputDir, cancellation.Token));
        Assert.True(observed);
    }

    private async Task<StaticSite> CreateAppAsync(RecordingArtifact artifact)
    {
        return await CreateAppAsync([artifact], staticPath: null);
    }

    private async Task<StaticSite> CreateAppAsync(IReadOnlyList<RecordingArtifact> artifacts, string? staticPath)
    {
        return await CreateAppCoreAsync(artifacts, staticPath);
    }

    private async Task<StaticSite> CreateAppCoreAsync(
        IReadOnlyList<RecordingArtifact> artifacts,
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
        app.UseContentSource<Post>(_ => items);
        TestArticleContents.MapSite(app);
        foreach (var artifact in artifacts)
        {
            artifact.Register(app);
        }

        await Task.CompletedTask;
        return app;
    }
}
