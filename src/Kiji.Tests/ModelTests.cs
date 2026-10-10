using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class ModelTests
{
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
            OutputDirectory = "relative-output",
        });
        Assert.Contains("The path must be absolute.", exception.Message, StringComparison.Ordinal);
    }

}
