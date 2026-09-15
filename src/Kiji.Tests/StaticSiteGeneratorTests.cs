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
            ContentDirectory = Path.Combine(_testDir, "contents"),
            StaticDirectory = Path.Combine(_testDir, "static"),
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
            ContentDirectory = Path.Combine(_testDir, "contents"),
            StaticDirectory = Path.Combine(_testDir, "static"),
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
}
