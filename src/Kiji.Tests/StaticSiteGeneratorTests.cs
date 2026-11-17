using Kiji.Generation;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticSiteGeneratorTests : IDisposable
{
    private readonly string _testDir;

    public StaticSiteGeneratorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"StaticSiteGeneratorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task RenderPagesAsync_PageOutputPathEscapingOutputDirectory_Throws()
    {
        var outputDir = Path.Combine(_testDir, "output");
        var options = new ResolvedSitePaths
        {
            OutputDirectory = outputDir,
        };

        var request = new PageRenderRequest(
            SourceIdentifier: "/blog/{Slug}/",
            ComponentType: typeof(StaticSiteGeneratorTests),
            Parameters: new Dictionary<string, object?>(),
            RoutePath: "/blog/../",
            OutputRelativePath: Path.Combine("blog", "..", "..", "evil.txt"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StaticSiteGenerator.RenderPagesAsync(
                options,
                [request],
                static async (_, output, _) => await output.WriteAsync("ignored"), previousOutputs: null, CancellationToken.None));

        Assert.Contains("Page output path", exception.Message, StringComparison.Ordinal);
        Assert.Contains("escapes the output directory", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_testDir, "evil.txt")));
    }

    [Fact]
    public async Task RenderPagesAsync_WritesUtf8WithoutBom()
    {
        var outputDir = Path.Combine(_testDir, "output");
        var options = new ResolvedSitePaths
        {
            OutputDirectory = outputDir,
        };

        var request = new PageRenderRequest(
            SourceIdentifier: "/",
            ComponentType: typeof(StaticSiteGeneratorTests),
            Parameters: new Dictionary<string, object?>(),
            RoutePath: "/",
            OutputRelativePath: "index.html");

        await StaticSiteGenerator.RenderPagesAsync(
            options,
            [request],
            static async (_, output, _) => await output.WriteAsync("<!doctype html><html>日本語</html>"), previousOutputs: null, CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(outputDir, "index.html"));
        Assert.True(bytes.Length >= 3);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Page output must not start with a UTF-8 BOM.");
        Assert.Equal("<!doctype html><html>日本語</html>", System.Text.Encoding.UTF8.GetString(bytes));
    }

    // Pages persist on writer threads after rendering moves on. A failed write must fail the
    // build with its own error, and stop further pages from rendering.
    [Fact]
    public async Task RenderPagesAsync_FailedWrite_ReportsItsErrorAndStopsRendering()
    {
        var outputDir = Path.Combine(_testDir, "output");
        // A directory where the first page's file belongs makes its write fail.
        Directory.CreateDirectory(Path.Combine(outputDir, "a", "index.html"));
        var requests = Enumerable.Range(0, 200).Select(index => new PageRenderRequest(
            SourceIdentifier: "/{Slug}/",
            ComponentType: typeof(StaticSiteGeneratorTests),
            Parameters: new Dictionary<string, object?>(),
            RoutePath: index == 0 ? "/a/" : $"/p{index}/",
            OutputRelativePath: index == 0 ? Path.Combine("a", "index.html") : Path.Combine($"p{index}", "index.html"))).ToArray();
        var rendered = 0;

        var exception = await Record.ExceptionAsync(() => StaticSiteGenerator.RenderPagesAsync(
            new ResolvedSitePaths { OutputDirectory = outputDir },
            requests,
            async (request, output, cancellationToken) =>
            {
                Interlocked.Increment(ref rendered);
                // Later pages take long enough for the failure to stop them.
                if (request.RoutePath != "/a/") { await Task.Delay(20, cancellationToken); }
                await output.WriteAsync("page");
            },
            previousOutputs: null, CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.True(rendered < requests.Length, $"Rendered {rendered} of {requests.Length} pages after the failure.");
    }
}
