using System.Text;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Tests for <see cref="ISiteArtifact"/> registration via <see cref="KijiApp.MapArtifact"/>
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
    public async Task BuildSiteAsync_WritesArtifact_AndExposesPagesAndRouteResolution()
    {
        var artifact = new RecordingArtifact("meta/info.txt");
        await using var app = await CreateAppAsync(artifact);

        await app.BuildSiteAsync();

        var written = await File.ReadAllTextAsync(Path.Combine(_outputDir, "meta", "info.txt"));
        Assert.Equal("artifact from zzzkan.me", written);

        var context = artifact.ObservedContext!;
        Assert.Contains(context.Pages, static page => page.RoutePath == "/blog/hello-world/");
        Assert.Contains(context.Pages, static page => page is { RoutePath: "/404.html", ExcludeFromSitemap: true });

        Assert.True(context.TryResolveRoute("hello-world", out var routePath));
        Assert.Equal("/blog/hello-world/", routePath);
        Assert.False(context.TryResolveRoute("no-such-content", out _));
    }

    [Fact]
    public async Task BuildSiteAsync_ArtifactPathEscapingOutputDirectory_Throws()
    {
        var artifact = new RecordingArtifact(Path.Combine("..", "evil.txt"));
        await using var app = await CreateAppAsync(artifact);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.BuildSiteAsync());

        Assert.Contains("escapes the output directory", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_testDir, "evil.txt")));
    }

    private async Task<KijiApp> CreateAppAsync(ISiteArtifact artifact)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = TestSitePaths.StaticDirectory;
        builder.Paths.Output = _outputDir;

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
        var posts = builder.AddContentSource<Post>(_ => items)
            .WithKey(static post => post.Slug);

        var app = builder.Build();
        TestArticleContents.MapSite(app, posts);
        app.MapArtifact(artifact);

        await Task.CompletedTask;
        return app;
    }
}
