using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class ModelTests
{
    [Fact]
    public async Task MarkdownContent_FailedRenderCanBeRetried()
    {
        var attempts = 0;
        var item = new MarkdownContent<string>(
            new MarkdownFileInfo("root", "post.md", "post.md", "", "post", "post", DateTime.UtcNow),
            "Post",
            (_, _) => ++attempts == 1
                ? Task.FromException<string>(new InvalidOperationException("temporary failure"))
                : Task.FromResult("recovered"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await item.RenderAsync());
        Assert.Equal("recovered", await item.RenderAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task MarkdownContent_RenderAsyncCachesOutput()
    {
        var renderCount = 0;
        var item = new MarkdownContent<string>(
            new MarkdownFileInfo(
                @"C:\test-contents",
                @"C:\test-contents\newer-post.md",
                "newer-post.md",
                string.Empty,
                "newer-post",
                "newer-post",
                new DateTime(2024, 2, 20, 0, 0, 0, DateTimeKind.Utc)),
            "Newer",
            (_, _) =>
            {
                renderCount++;
                return Task.FromResult("<p>Newer</p>");
            });
        Assert.Equal("<p>Newer</p>", await item.RenderAsync());
        Assert.Equal("<p>Newer</p>", await item.RenderAsync());
        Assert.Equal(1, renderCount);
    }

    [Fact]
    public void ResolvedSitePaths_AllowsMissingContentsAndStaticDirectories()
    {
        var missingContents = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var missingStatic = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");

        var options = new ResolvedSitePaths
        {
            ContentDirectory = missingContents,
            StaticDirectory = missingStatic,
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
        };

        Assert.Equal(Path.GetFullPath(missingContents), options.ContentDirectory);
        Assert.Equal(Path.GetFullPath(missingStatic), options.StaticDirectory);
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

    [Fact]
    public void ResolvedSitePaths_RejectsRelativeImageCacheDirectory()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ResolvedSitePaths
        {
            ContentDirectory = Path.GetTempPath(),
            StaticDirectory = Path.GetTempPath(),
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
            ImageCacheDirectory = "relative-cache",
        });

        Assert.Contains("The path must be absolute.", exception.Message, StringComparison.Ordinal);
    }
}
