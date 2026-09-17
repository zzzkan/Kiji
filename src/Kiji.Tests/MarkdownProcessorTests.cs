using Kiji.Markdown;
using Kiji.Assets;
using Kiji.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Kiji.Tests;

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
    public async Task SequentialDocuments_ResetImageLoadingState()
    {
        var processor = CreateProcessor();
        var first = CreateMarkdownFile("first.md", "![First](a.png)\n\n![Second](a.png)");
        var second = CreateMarkdownFile("second.md", "![Next](a.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "a.png"), 32, 32);
        var firstHtml = await WithPageContextAsync("/first/", "first", () => ProcessFileAsync(processor, first));
        var secondHtml = await WithPageContextAsync("/second/", "second", () => ProcessFileAsync(processor, second));
        Assert.Contains("loading=\"eager\"", firstHtml, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", firstHtml, StringComparison.Ordinal);
        Assert.Contains("loading=\"eager\"", secondHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("loading=\"lazy\"", secondHtml, StringComparison.Ordinal);
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
            () => ProcessFileAsync(CreateProcessor(), mdPath));

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
    public async Task ProcessAsync_EncodedImageWithQueryAndFragment_IsBundled()
    {
        var mdPath = CreateMarkdownFile("encoded.md", "![Photo](my%20photo.png?v=1#preview)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "my photo.png"), 100, 60);
        var html = await WithPageContextAsync("/encoded/", "encoded", () => ProcessFileAsync(CreateProcessor(), mdPath));
        Assert.Contains("src=\"./my%20photo.png.", html, StringComparison.Ordinal);
        Assert.Contains("srcset=\"./my%20photo.png.", html, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_outputDir, "encoded"), "my photo.png.*.webp"));
    }

    [Fact]
    public async Task ProcessAsync_MissingImage_ThrowsWithFileAndUrl()
    {
        var mdPath = CreateMarkdownFile("broken.md", "![Missing](missing.png)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WithPageContextAsync("/p/", "p", () => ProcessFileAsync(CreateProcessor(), mdPath)));

        Assert.Contains("missing.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains("broken.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ImageOutsideContentDirectory_Throws()
    {
        var mdPath = CreateMarkdownFile("escape.md", "![Escape](../escape.png)");
        await CreateTestImageAsync(Path.Combine(_testDir, "escape.png"), 320, 240);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WithPageContextAsync("/p/", "p", () => ProcessFileAsync(CreateProcessor(), mdPath)));

        Assert.Contains("resolves outside", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ImageWithoutPageRenderContext_Throws()
    {
        var mdPath = CreateMarkdownFile("no-context.md", "![Used](used.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 320, 240);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProcessFileAsync(CreateProcessor(), mdPath));

        Assert.Contains("page render", exception.Message, StringComparison.Ordinal);
    }

    private MarkdownProcessor CreateProcessor()
    {
        return new MarkdownProcessor(new ResolvedSitePaths
        {
            ContentDirectory = _testFilesDir,
            StaticDirectory = _testDir,
            OutputDirectory = _outputDir,
            ImageCacheDirectory = _cacheDir,
        }, new ImageProcessor());
    }

    private static Task<string> ProcessFileAsync(MarkdownProcessor processor, string path)
    {
        var body = MarkdownFrontMatterParser.RemoveFrontMatter(File.ReadAllText(path));
        return processor.ProcessBodyAsync(path, body);
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
