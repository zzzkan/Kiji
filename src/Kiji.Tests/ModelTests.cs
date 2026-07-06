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
            new MarkdownFileInfo(@"C:\test-contents", @"C:\test-contents\first.md", "first.md", string.Empty, "first", DateTime.UtcNow),
            "First",
            static (_, _) => Task.FromResult("first"));
        var second = new MarkdownContent<string>(
            new MarkdownFileInfo(@"C:\test-contents", @"C:\test-contents\second.md", "second.md", string.Empty, "second", DateTime.UtcNow),
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
            AssetFileNameBase = "test-image.png",
            FileName = "test-image",
            OriginalWidth = 1920,
            OriginalHeight = 1080,
            AvailableWidths = [320, 640, 960, 1280, 1920],
            AspectRatio = 1920.0 / 1080.0,
            ContentHash = "abc12345"
        };

        // Assert
        Assert.Equal("test-image.png", imageInfo.AssetFileNameBase);
        Assert.Equal("test-image", imageInfo.FileName);
        Assert.Equal(1920, imageInfo.OriginalWidth);
        Assert.Equal(1080, imageInfo.OriginalHeight);
        Assert.Equal(5, imageInfo.AvailableWidths.Length);
        Assert.Equal(1920.0 / 1080.0, imageInfo.AspectRatio);
        Assert.Equal("abc12345", imageInfo.ContentHash);
    }

    [Fact]
    public void ProcessedImageInfo_AspectRatio_CalculatedCorrectly()
    {
        // Arrange
        var imageInfo = new ProcessedImageInfo
        {
            AssetFileNameBase = "wide.png",
            FileName = "wide",
            OriginalWidth = 1600,
            OriginalHeight = 900,
            AvailableWidths = [1600],
            AspectRatio = 1600.0 / 900.0,
            ContentHash = "abc"
        };

        // Assert - 16:9 aspect ratio
        Assert.Equal(16.0 / 9.0, imageInfo.AspectRatio, precision: 4);
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
        Assert.Equal("_assets", options.AssetsDirectoryName);
    }

    [Fact]
    public void SsgOptions_RejectsMissingContentsDirectory()
    {
        var exception = Assert.Throws<DirectoryNotFoundException>(() => new SsgOptions
        {
            ContentsPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
            StaticPath = Path.GetTempPath(),
            OutputPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
        });
        Assert.Contains("Directory not found", exception.Message, StringComparison.Ordinal);
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
    public void SsgOptions_RejectsInvalidAssetsDirectoryName()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SsgOptions
        {
            ContentsPath = Path.GetTempPath(),
            StaticPath = Path.GetTempPath(),
            OutputPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid():N}"),
            AssetsDirectoryName = "bad/name",
        });

        Assert.Contains("AssetsDirectoryName", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
