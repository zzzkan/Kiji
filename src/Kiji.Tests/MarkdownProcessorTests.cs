using Kiji.Markdown;
using Kiji.Assets;
using Kiji.Rendering;
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
    private readonly string _outputDir;
    private readonly string _cacheDir;

    public MarkdownProcessorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownProcessorTests_{Guid.NewGuid():N}");
        _testFilesDir = Path.Combine(_testDir, "files");
        _outputDir = Path.Combine(_testDir, "output");
        _cacheDir = Path.Combine(_testDir, "cache");
        Directory.CreateDirectory(_testFilesDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

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

        var htmlContent = await CreateProcessor().ProcessAsync(mdPath);

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProcessor().ProcessAsync(mdPath));
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

        var htmlContent = await CreateProcessor().ProcessAsync(mdPath);

        Assert.Contains("<strong>Bold</strong>", htmlContent);
        Assert.Contains("<em>italic</em>", htmlContent);
        Assert.Contains("<ul>", htmlContent);
        Assert.Contains("<li>Item 1</li>", htmlContent);
        Assert.Contains("<pre><code class=\"language-csharp\">", htmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ReferencedImage_MaterializedIntoPageOutputWithRelativeUrl()
    {
        var mdPath = CreateMarkdownFile("with-image.md", "![Used](used.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 1200, 800);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "unused.png"), 800, 600);

        var htmlContent = await WithPageContextAsync(
            "/blog/with-image/",
            Path.Combine("blog", "with-image"),
            () => CreateProcessor().ProcessAsync(mdPath));

        var pageOutputDir = Path.Combine(_outputDir, "blog", "with-image");
        var generatedFiles = Directory.GetFiles(pageOutputDir, "*.webp")
            .Select(Path.GetFileName)
            .Cast<string>()
            .ToArray();

        Assert.Contains("src=\"./used.png.", htmlContent, StringComparison.Ordinal);
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("used.png.", StringComparison.Ordinal));
        Assert.DoesNotContain(generatedFiles, static fileName => fileName.StartsWith("unused.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_ImageInSubdirectory_ResolvesAndKeepsRelativeStructure()
    {
        Directory.CreateDirectory(Path.Combine(_testFilesDir, "images"));
        var mdPath = CreateMarkdownFile("subdir.md", "![Photo](./images/photo.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "images", "photo.png"), 640, 480);

        var htmlContent = await WithPageContextAsync(
            "/blog/subdir/",
            Path.Combine("blog", "subdir"),
            () => CreateProcessor().ProcessAsync(mdPath));

        var imageOutputDir = Path.Combine(_outputDir, "blog", "subdir", "images");
        Assert.Contains("src=\"./images/photo.png.", htmlContent, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(imageOutputDir, "photo.png.*.webp"));
    }

    [Fact]
    public async Task ProcessAsync_ImagesAreCachedAcrossOutputCleans()
    {
        var mdPath = CreateMarkdownFile("cached.md", "![Used](used.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 800, 600);

        await WithPageContextAsync("/p/", "p", () => CreateProcessor().ProcessAsync(mdPath));

        var cacheFiles = Directory.GetFiles(_cacheDir, "*.webp", SearchOption.AllDirectories);
        Assert.NotEmpty(cacheFiles);
        var cacheWriteTimes = cacheFiles.ToDictionary(static f => f, static f => File.GetLastWriteTimeUtc(f));

        // Simulate a clean build: output wiped, cache survives, no re-encode.
        Directory.Delete(_outputDir, recursive: true);
        await Task.Delay(100);

        await WithPageContextAsync("/p/", "p", () => CreateProcessor().ProcessAsync(mdPath));

        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_outputDir, "p"), "*.webp"));
        foreach (var (file, writeTime) in cacheWriteTimes)
        {
            Assert.Equal(writeTime, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public async Task ProcessAsync_MissingImage_ThrowsWithFileAndUrl()
    {
        var mdPath = CreateMarkdownFile("broken.md", "![Missing](missing.png)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WithPageContextAsync("/p/", "p", () => CreateProcessor().ProcessAsync(mdPath)));

        Assert.Contains("missing.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains("broken.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ImageOutsideContentDirectory_Throws()
    {
        var mdPath = CreateMarkdownFile("escape.md", "![Escape](../escape.png)");
        await CreateTestImageAsync(Path.Combine(_testDir, "escape.png"), 320, 240);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WithPageContextAsync("/p/", "p", () => CreateProcessor().ProcessAsync(mdPath)));

        Assert.Contains("resolves outside", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ImageWithoutPageRenderContext_Throws()
    {
        var mdPath = CreateMarkdownFile("no-context.md", "![Used](used.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 320, 240);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateProcessor().ProcessAsync(mdPath));

        Assert.Contains("page render", exception.Message, StringComparison.Ordinal);
    }

    private MarkdownProcessor CreateProcessor()
    {
        return new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _testFilesDir,
            StaticPath = _testDir,
            OutputPath = _outputDir,
            ImageCachePath = _cacheDir,
        }, new ImageProcessor());
    }

    private string CreateMarkdownFile(string fileName, string body)
    {
        var content = $"""
            ---
            title: Test
            createdAt: 2024-01-15
            ---

            {body}
            """;

        var path = Path.Combine(_testFilesDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<T> WithPageContextAsync<T>(string routePath, string outputRelativeDirectory, Func<Task<T>> action)
    {
        PageRenderContext.SetCurrent(new PageRenderContext
        {
            RoutePath = routePath,
            OutputRelativeDirectory = outputRelativeDirectory,
        });

        try
        {
            return await action();
        }
        finally
        {
            PageRenderContext.SetCurrent(null);
        }
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

        await image.SaveAsPngAsync(path);
    }
}
