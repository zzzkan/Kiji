using Kiji.Markdown;
using Kiji.Assets;
using Kiji.Rendering;
using SkiaSharp;
using Xunit;
using System.Text.RegularExpressions;

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
    public async Task SequentialDocuments_ResetFirstImageLoadingAndShareImageUrls()
    {
        var processor = CreateProcessor();
        var first = CreateMarkdownFile("first.md", "![First](a.png)\n\n![Second](a.png)");
        var second = CreateMarkdownFile("second.md", "![Next](a.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "a.png"), 32, 32);
        var firstHtml = await WithPageContextAsync("/first/", () => ProcessFileAsync(processor, first));
        var secondHtml = await WithPageContextAsync("/second/", () => ProcessFileAsync(processor, second));
        Assert.Contains("loading=\"eager\"", firstHtml, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", firstHtml, StringComparison.Ordinal);
        Assert.Contains("sizes=\"auto, ", firstHtml, StringComparison.Ordinal);
        Assert.Equal(ImageSource(firstHtml), ImageSource(secondHtml));
        Assert.StartsWith("/_images/", ImageSource(firstHtml), StringComparison.Ordinal);
        Assert.Contains("loading=\"eager\"", secondHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("loading=\"lazy\"", secondHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("sizes=\"auto, ", secondHtml, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ProcessAsync_ReferencedImage_MaterializedIntoSharedOutputWithRootRelativeUrl()
    {
        var mdPath = CreateMarkdownFile("with-image.md", "![Used](used.png)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "used.png"), 1200, 800);
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "unused.png"), 800, 600);

        var htmlContent = await WithPageContextAsync(
            "/blog/with-image/",
            () => ProcessFileAsync(CreateProcessor(), mdPath));

        var generatedFiles = Directory.GetFiles(Path.Combine(_outputDir, "_images"), "*.webp", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Cast<string>()
            .ToArray();

        Assert.StartsWith("/_images/", ImageSource(htmlContent), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_outputDir, "blog")));
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("used.png.", StringComparison.Ordinal));
        Assert.DoesNotContain(generatedFiles, static fileName => fileName.StartsWith("unused.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_EncodedImageWithQueryAndFragment_IsBundled()
    {
        var mdPath = CreateMarkdownFile("encoded.md", "![Photo](my%20photo.png?v=1#preview)");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "my photo.png"), 100, 60);
        var html = await WithPageContextAsync("/encoded/", () => ProcessFileAsync(CreateProcessor(), mdPath));
        Assert.StartsWith("/_images/", ImageSource(html), StringComparison.Ordinal);
        Assert.Contains("/my%20photo.png.", html, StringComparison.Ordinal);
        Assert.Contains("srcset=\"/_images/", html, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(_outputDir, "my photo.png.*.webp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ProcessAsync_MissingImage_ThrowsWithFileAndUrl()
    {
        var mdPath = CreateMarkdownFile("broken.md", "![Missing](missing.png)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WithPageContextAsync("/p/", () => ProcessFileAsync(CreateProcessor(), mdPath)));

        Assert.Contains("missing.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains("broken.md", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateArticleDirectories_ShareParentImageAndHonorBasePath(bool tracked)
    {
        var first = CreateMarkdownFile("a/index.md", "![Photo](../shared/my%20photo.png)");
        var second = CreateMarkdownFile("b/index.md", "![Photo](../shared/./my%20photo.png?version=1#preview)");
        Directory.CreateDirectory(Path.Combine(_testFilesDir, "shared"));
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "shared", "my photo.png"), 1200, 800);
        var processor = CreateProcessor(TestArticleContents.CreateSiteInfoWithBasePath());
        var results = await Task.WhenAll(
            WithPageContextAsync("/a/", () => ProcessFileAsync(processor, first), tracked),
            WithPageContextAsync("/b/", () => ProcessFileAsync(processor, second), tracked));
        Assert.Equal(results[0], results[1]);
        Assert.StartsWith("/kiji/_images/", ImageSource(results[0]), StringComparison.Ordinal);
        var variants = Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories);
        Assert.Equal(4, variants.Length);
        Assert.Single(variants.Select(Path.GetDirectoryName).Distinct());
        Assert.All(variants, path => Assert.Contains(
            "/kiji/" + string.Join('/', Path.GetRelativePath(_outputDir, path).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)),
            results[0], StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameNamedSources_CustomProcessorOutputsDoNotCollide()
    {
        var first = CreateMarkdownFile("a/index.md", "![Photo](photo.png)");
        var second = CreateMarkdownFile("b/index.md", "![Photo](photo.png)");
        await File.WriteAllBytesAsync(Path.Combine(_testFilesDir, "a", "photo.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_testFilesDir, "b", "photo.png"), [2]);
        var processor = CreateProcessor(imageProcessor: new TestSite.PageServices.TrackingImageProcessor());
        var results = await Task.WhenAll(
            WithPageContextAsync("/a/", () => ProcessFileAsync(processor, first)),
            WithPageContextAsync("/b/", () => ProcessFileAsync(processor, second)));
        Assert.NotEqual(ImageSource(results[0]), ImageSource(results[1]));
        Assert.Equal(2, Directory.GetFiles(_outputDir, "custom.svg", SearchOption.AllDirectories).Length);
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

    private MarkdownProcessor CreateProcessor(SiteInfo? site = null, IImageProcessor? imageProcessor = null)
    {
        return new MarkdownProcessor(new ResolvedSitePaths
        {
            OutputDirectory = _outputDir,
            ImageCacheDirectory = _cacheDir,
        }, new SiteExecutionPaths(_testDir), site ?? TestArticleContents.CreateSiteInfo(), imageProcessor ?? new ImageProcessor());
    }

    private static Task<string> ProcessFileAsync(MarkdownProcessor processor, string path)
    {
        var body = MarkdownSourceReader.Read<FrontMatter>(new FileInfo(path), new MarkdownOptions().CreateDeserializer()).Body;
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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<T> WithPageContextAsync<T>(string routePath, Func<Task<T>> action, bool tracked = false)
    {
        PageRenderContext.SetCurrent(new PageRenderContext
        {
            RoutePath = routePath,
            Dependencies = tracked ? new Kiji.Generation.BuildDependencyRecorder() : null,
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

    private static string ImageSource(string html) => Regex.Match(html, "src=\"([^\"]+)\"").Groups[1].Value;

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, new SKColor((byte)(x % 255), (byte)(y % 255), 128));
            }
        }

        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, encoded.ToArray());
    }
}
