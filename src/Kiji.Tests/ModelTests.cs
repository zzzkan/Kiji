using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class ModelTests
{
    [Fact]
    public void SitePaths_StaticDirectoryDefaultsToWwwroot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"site-{Guid.NewGuid():N}");
        var paths = new SitePaths(root);

        Assert.Equal("wwwroot", paths.StaticDirectory);
        Assert.Equal(Path.Combine(root, "wwwroot"), paths.ResolveForDevelopment().StaticDirectory);
    }

    [Fact]
    public async Task MarkdownContent_RetriesFailedRenderAndCachesSuccess()
    {
        var attempts = 0;
        var item = new MarkdownContent<string>(
            new FileInfo("post.md"),
            "Post",
            string.Empty,
            (_, _) => ++attempts == 1
                ? Task.FromException<string>(new InvalidOperationException("temporary failure"))
                : Task.FromResult("recovered"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await item.RenderAsync());
        Assert.Equal("recovered", await item.RenderAsync());
        Assert.Equal("recovered", await item.RenderAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ResolvedSitePaths_RejectsRelativeOutputDirectory()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ResolvedSitePaths
        {
            ContentDirectory = Path.GetTempPath(),
            StaticDirectory = Path.GetTempPath(),
            OutputDirectory = "relative-output",
        });
        Assert.Contains("The path must be absolute.", exception.Message, StringComparison.Ordinal);
    }

}
