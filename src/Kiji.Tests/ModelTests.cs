using Kiji.Markdown;
using Xunit;
using Kiji.Assets;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for model classes.
/// </summary>
public sealed class ModelTests
{
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
        IReadOnlyList<MarkdownContent<string>> contents = [item];

        Assert.Single(contents);
        Assert.Equal("Newer", contents[0].FrontMatter);
        Assert.Equal("<p>Newer</p>", await item.RenderAsync());
        Assert.Equal("<p>Newer</p>", await item.RenderAsync());
        Assert.Equal(1, renderCount);
    }

    [Fact]
    public void MarkdownContents_PreservesItemOrder()
    {
        var first = new MarkdownContent<string>(
            new MarkdownFileInfo(@"C:\test-contents", @"C:\test-contents\first.md", "first.md", string.Empty, "first", "first", DateTime.UtcNow),
            "First",
            static (_, _) => Task.FromResult("first"));
        var second = new MarkdownContent<string>(
            new MarkdownFileInfo(@"C:\test-contents", @"C:\test-contents\second.md", "second.md", string.Empty, "second", "second", DateTime.UtcNow),
            "Second",
            static (_, _) => Task.FromResult("second"));
        IReadOnlyList<MarkdownContent<string>> contents = [first, second];

        Assert.Equal(["First", "Second"], contents.Select(static item => item.FrontMatter));
    }

    #region FrontMatter Tests

    [Fact]
    public void FrontMatter_AllPropertiesNullable_DefaultsToNull()
    {
        // Arrange & Act
        var frontMatter = new FrontMatter();

        // Assert
        Assert.Null(frontMatter.Title);
        Assert.Null(frontMatter.CreatedAt);
        Assert.Null(frontMatter.UpdatedAt);
        Assert.Null(frontMatter.Tags);
    }

    [Fact]
    public void FrontMatter_CanSetAllProperties()
    {
        // Arrange & Act
        var frontMatter = new FrontMatter
        {
            Title = "Test Title",
            CreatedAt = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.FromHours(9)),
            UpdatedAt = new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.FromHours(9)),
            Tags = ["tag1", "tag2"]
        };

        // Assert
        Assert.Equal("Test Title", frontMatter.Title);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.FromHours(9)), frontMatter.CreatedAt);
        Assert.Equal(new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.FromHours(9)), frontMatter.UpdatedAt);
        Assert.Equal(2, frontMatter.Tags!.Count);
    }

    #endregion

    #region ProcessedImageInfo Tests

    [Fact]
    public void ProcessedImageInfo_InitializesCorrectly()
    {
        // Arrange & Act
        var imageInfo = new ProcessedImageInfo
        {
            OriginalWidth = 1920,
            OriginalHeight = 1080,
            Variants =
            [
                new ImageVariant("test-image.png.abc12345.320w.webp", 320),
                new ImageVariant("test-image.png.abc12345.1920w.webp", 1920),
            ],
        };

        // Assert
        Assert.Equal(1920, imageInfo.OriginalWidth);
        Assert.Equal(1080, imageInfo.OriginalHeight);
        Assert.Equal(2, imageInfo.Variants.Count);
        Assert.Equal("test-image.png.abc12345.1920w.webp", imageInfo.Variants[^1].FileName);
        Assert.Equal(1920, imageInfo.Variants[^1].Width);
    }

    #endregion

    #region SsgOptions Tests

    [Fact]
    public void SsgOptions_InitializesCorrectly()
    {
        // Arrange & Act
        var outputPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}");
        var options = new SsgOptions
        {
            ContentsPath = Path.GetTempPath(),
            StaticPath = Path.GetTempPath(),
            OutputPath = outputPath,
        };

        // Assert
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), options.ContentsPath);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), options.StaticPath);
        Assert.Equal(Path.GetFullPath(outputPath), options.OutputPath);
        Assert.Null(options.ImageCachePath);
    }

    [Fact]
    public void SsgOptions_AllowsMissingContentsAndStaticDirectories()
    {
        var missingContents = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var missingStatic = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");

        var options = new SsgOptions
        {
            ContentsPath = missingContents,
            StaticPath = missingStatic,
            OutputPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
        };

        Assert.Equal(Path.GetFullPath(missingContents), options.ContentsPath);
        Assert.Equal(Path.GetFullPath(missingStatic), options.StaticPath);
    }

    [Fact]
    public void SsgOptions_RejectsRelativeOutputDirectory()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SsgOptions
        {
            ContentsPath = Path.GetTempPath(),
            StaticPath = Path.GetTempPath(),
            OutputPath = "relative-output",
        });
        Assert.Contains("The path must be absolute.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SsgOptions_RejectsRelativeImageCachePath()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SsgOptions
        {
            ContentsPath = Path.GetTempPath(),
            StaticPath = Path.GetTempPath(),
            OutputPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
            ImageCachePath = "relative-cache",
        });

        Assert.Contains("The path must be absolute.", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
