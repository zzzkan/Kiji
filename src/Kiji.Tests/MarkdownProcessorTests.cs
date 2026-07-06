using Kiji.Markdown;
using Kiji.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="MarkdownProcessor"/>.
/// </summary>
public sealed class MarkdownProcessorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilesDir;

    public MarkdownProcessorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownProcessorTests_{Guid.NewGuid():N}");
        _testFilesDir = Path.Combine(_testDir, "files");
        Directory.CreateDirectory(_testFilesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region ProcessAsync Tests

    [Fact]
    public async Task ProcessAsync_ValidMarkdown_ConvertsToHtml()
    {
        var mdContent = """
            ---
            title: Test Post
            createdAt: 2024-01-15
            tags:
              - test
              - sample
            ---

            # Hello World

            This is a test post.
            """;

        var mdPath = Path.Combine(_testFilesDir, "test.md");
        File.WriteAllText(mdPath, mdContent);

        var processor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _testDir,
            AssetsDirectoryName = "test-assets",
        }, new ImageProcessor());

        var htmlContent = await processor.ProcessAsync(mdPath);

        Assert.Contains("<h1", htmlContent);
        Assert.Contains("Hello World", htmlContent);
        Assert.Contains("This is a test post.", htmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_NoFrontMatter_ThrowsInvalidOperationException()
    {
        var mdContent = """
            # Hello World

            This is a test post without front matter.
            """;

        var mdPath = Path.Combine(_testFilesDir, "no-frontmatter.md");
        File.WriteAllText(mdPath, mdContent);

        var processor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _testDir,
            AssetsDirectoryName = "test-assets",
        }, new ImageProcessor());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(mdPath));
    }

    [Fact]
    public async Task ProcessAsync_MarkdownElements_ConvertsToHtml()
    {
        var mdContent = """
            ---
            title: Rich Content
            createdAt: 2024-01-15
            ---

            **Bold** and *italic* text.

            - Item 1
            - Item 2

            ```csharp
            var x = 1;
            ```
            """;

        var mdPath = Path.Combine(_testFilesDir, "rich.md");
        File.WriteAllText(mdPath, mdContent);

        var processor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _testDir,
            AssetsDirectoryName = "test-assets",
        }, new ImageProcessor());

        var htmlContent = await processor.ProcessAsync(mdPath);

        Assert.Contains("<strong>Bold</strong>", htmlContent);
        Assert.Contains("<em>italic</em>", htmlContent);
        Assert.Contains("<ul>", htmlContent);
        Assert.Contains("<li>Item 1</li>", htmlContent);
        Assert.Contains("<pre><code class=\"language-csharp\">", htmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_OnlyProcessesReferencedImagesUnderContentHashDirectory()
    {
        var mdContent = """
            ---
            title: Post With Image
            createdAt: 2024-01-15
            ---

            ![Used](used.png)
            """;

        var mdPath = Path.Combine(_testFilesDir, "with-image.md");
        File.WriteAllText(mdPath, mdContent);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 1200, 800);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "unused.png"), 800, 600);

        var processor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _testDir,
            AssetsDirectoryName = "test-assets",
        }, new ImageProcessor());

        var htmlContent = await processor.ProcessAsync(mdPath);
        var assetDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(_testDir, "test-assets")));
        var contentKey = Path.GetFileName(assetDirectory);
        var generatedFiles = Directory.GetFiles(assetDirectory, "*.webp")
            .Select(Path.GetFileName)
            .Where(static fileName => fileName is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains($"/test-assets/{contentKey}/used.png.", htmlContent, StringComparison.Ordinal);
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("used.png.", StringComparison.Ordinal));
        Assert.DoesNotContain(generatedFiles, static fileName => fileName.StartsWith("unused.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_MixedExtensions_UsesExactReferencedFileNames()
    {
        var mdContent = """
            ---
            title: Mixed Extensions
            createdAt: 2024-01-15
            ---

            ![Jpg](foo.jpg)

            ![Png](foo.png)
            """;

        var mdPath = Path.Combine(_testFilesDir, "mixed.md");
        File.WriteAllText(mdPath, mdContent);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "foo.jpg"), 900, 450);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "foo.png"), 400, 200);

        var processor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _testDir,
            AssetsDirectoryName = "test-assets",
        }, new ImageProcessor());

        var htmlContent = await processor.ProcessAsync(mdPath);
        var assetDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(_testDir, "test-assets")));
        var contentKey = Path.GetFileName(assetDirectory);

        Assert.Contains($"/test-assets/{contentKey}/foo.jpg.", htmlContent, StringComparison.Ordinal);
        Assert.Contains($"/test-assets/{contentKey}/foo.png.", htmlContent, StringComparison.Ordinal);
    }

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32((byte)(x % 255), (byte)(y % 255), 128);
            }
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                await image.SaveAsJpegAsync(path);
                break;

            default:
                await image.SaveAsPngAsync(path);
                break;
        }
    }

    #endregion
}
