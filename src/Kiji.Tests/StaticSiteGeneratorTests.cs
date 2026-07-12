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
    public async Task GenerateAsync_PageOutputPathEscapingOutputDirectory_Throws()
    {
        var outputDir = Path.Combine(_testDir, "output");
        var options = new SsgOptions
        {
            ContentsPath = _testDir,
            StaticPath = Path.Combine(_testDir, "static"),
            OutputPath = outputDir,
        };

        var request = new PageRenderRequest(
            SourceIdentifier: "/blog/{Slug}/",
            ComponentType: typeof(StaticSiteGeneratorTests),
            Parameters: new Dictionary<string, object?>(),
            RoutePath: "/blog/../",
            OutputRelativePath: Path.Combine("blog", "..", "..", "evil.txt"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StaticSiteGenerator.GenerateAsync(
                options,
                [request],
                static async (_, output, _) => await output.WriteAsync("ignored")));

        Assert.Contains("Page output path", exception.Message, StringComparison.Ordinal);
        Assert.Contains("escapes the output directory", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_testDir, "evil.txt")));
    }
}