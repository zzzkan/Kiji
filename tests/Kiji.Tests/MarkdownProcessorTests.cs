using System.Text.RegularExpressions;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Imaging;
using Kiji.Incremental;
using Kiji.Markdown;
using Kiji.Paths;
using Kiji.Rendering;
using SkiaSharp;
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
        Assert.Contains(mdPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_testFilesDir, "missing.png"), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/kiji/", false)]
    [InlineData("/kiji/", true)]
    [InlineData("/%E6%97%A5%E6%9C%AC/", false)]
    [InlineData("/%E6%97%A5%E6%9C%AC/", true)]
    public async Task SeparateArticleDirectories_ShareParentImageAndHonorBasePath(string basePath, bool tracked)
    {
        var first = CreateMarkdownFile("a/index.md", "![Photo](../shared/my%20photo.png)");
        var second = CreateMarkdownFile("b/index.md", "![Photo](../shared/./my%20photo.png?version=1#preview)");
        Directory.CreateDirectory(Path.Combine(_testFilesDir, "shared"));
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "shared", "my photo.png"), 1200, 800);
        var processor = CreateProcessor(new SiteInfo { Name = "Images", BaseUrl = new Uri("https://example.com" + basePath) });
        var results = await Task.WhenAll(
            WithPageContextAsync("/a/", () => ProcessFileAsync(processor, first), tracked),
            WithPageContextAsync("/b/", () => ProcessFileAsync(processor, second), tracked));
        Assert.Equal(results[0], results[1]);
        Assert.StartsWith(basePath + "_images/", ImageSource(results[0]), StringComparison.Ordinal);
        var variants = Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories);
        Assert.Equal(4, variants.Length);
        Assert.Equal(Path.Combine(_outputDir, "_images"), Assert.Single(variants.Select(Path.GetDirectoryName).Distinct()));
        Assert.All(variants, path => Assert.Contains(
            basePath + string.Join('/', Path.GetRelativePath(_outputDir, path).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)),
            results[0], StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameNamedSources_CustomProcessorOutputsDoNotCollide()
    {
        var first = CreateMarkdownFile("a/index.md", "![Photo](photo.png)");
        var second = CreateMarkdownFile("b/index.md", "![Photo](photo.png)");
        await File.WriteAllBytesAsync(Path.Combine(_testFilesDir, "a", "photo.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_testFilesDir, "b", "photo.png"), [2]);
        var processor = CreateProcessor(imageProcessor: new TestSite.PageServices.VaryingImageProcessor());
        var results = await Task.WhenAll(
            WithPageContextAsync("/a/", () => ProcessFileAsync(processor, first)),
            WithPageContextAsync("/b/", () => ProcessFileAsync(processor, second)));
        Assert.NotEqual(ImageSource(results[0]), ImageSource(results[1]));
        Assert.Equal(2, Directory.GetFiles(_outputDir, "image.svg", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("../unwatched.png")]
    [InlineData("../../outside.png")]
    [InlineData("%5Csrv%5Cshared%5Clogo.png")]
    [InlineData(@"\srv\shared\logo.png")]
    [InlineData("%2Fetc%2Flogo.png")]
    public async Task ImagesOutsideWatchedProjectRoots_FailBeforePublishing(string url)
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "unwatched.png"), [1, 2, 3]);
        var markdown = CreateMarkdownFile("invalid.md", $"![Invalid]({url})");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPageContextAsync("/invalid/", () => ProcessFileAsync(CreateProcessor(), markdown)));
        Assert.Contains("invalid.md", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageLinksCannotEscapeWatchedRoots(bool directoryLink)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires privileges on Windows.");
        var outside = Path.Combine(_testDir, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllBytesAsync(Path.Combine(outside, "photo.png"), [1, 2, 3]);
        var link = Path.Combine(_testFilesDir, directoryLink ? "linked" : "linked.png");
        if (directoryLink) { Directory.CreateSymbolicLink(link, outside); }
        else { File.CreateSymbolicLink(link, Path.Combine(outside, "photo.png")); }
        var markdown = CreateMarkdownFile("linked.md", $"![Link]({(directoryLink ? "linked/photo.png" : "linked.png")})");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPageContextAsync("/links/", () => ProcessFileAsync(CreateProcessor(), markdown)));
        Assert.Contains("symbolic links", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RelativeMarkdownFilename_ResolvesFromTheProjectRoot()
    {
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "photo.png"), 32, 32);
        var html = await WithPageContextAsync("/relative/", () =>
            CreateProcessor().ProcessBodyAsync("post.md", "![Photo](files/photo.png)"));
        Assert.StartsWith("/_images/", ImageSource(html), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyReadPaths_StillRejectReplacementWithSymbolicLinks(bool directoryLink)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires privileges on Windows.");
        var shared = Path.Combine(_testFilesDir, "shared");
        var outside = Path.Combine(_testDir, "outside");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(outside);
        var image = Path.Combine(shared, "photo.png");
        await CreateTestImageAsync(image, 32, 32);
        await CreateTestImageAsync(Path.Combine(outside, "photo.png"), 32, 32);
        var markdown = CreateMarkdownFile("cached.md", "![Photo](shared/photo.png)");
        var processor = CreateProcessor();
        await WithPageContextAsync("/cached/", () => ProcessFileAsync(processor, markdown));

        if (directoryLink)
        {
            Directory.Delete(shared, recursive: true);
            Directory.CreateSymbolicLink(shared, outside);
        }
        else
        {
            File.Delete(image);
            File.CreateSymbolicLink(image, Path.Combine(outside, "photo.png"));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPageContextAsync("/cached/", () => ProcessFileAsync(processor, markdown)));
        Assert.Contains("symbolic links", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaseAliasesOnInsensitiveFilesystems_ShareOneImageFamily()
    {
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "Photo.png"), 32, 32);
        Assert.SkipUnless(File.Exists(Path.Combine(_testFilesDir, "photo.png")), "Requires a case-insensitive filesystem.");
        var markdown = CreateMarkdownFile("cases.md", "![First](Photo.png)\n\n![Second](photo.png)");
        var html = await WithPageContextAsync("/cases/", () => ProcessFileAsync(CreateProcessor(), markdown));
        var urls = Regex.Matches(html, "src=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(2, urls.Length);
        Assert.Equal(urls[0], urls[1]);
        Assert.Single(Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AddedImage_IsVisibleBeforeSnapshotInvalidation()
    {
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "first.png"), 32, 32);
        var processor = CreateProcessor();
        await WithPageContextAsync("/first/", () => processor.ProcessBodyAsync(
            Path.Combine(_testFilesDir, "first.md"), "![First](first.png)"));
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "added.png"), 32, 32);
        var html = await WithPageContextAsync("/second/", () => processor.ProcessBodyAsync(
            Path.Combine(_testFilesDir, "second.md"), "![Added](added.png)"));
        Assert.Contains("added.png.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaseDistinctAddedImage_DoesNotResolveToPreviouslyReadLowercaseName()
    {
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "photo.png"), 32, 32);
        Assert.SkipWhen(File.Exists(Path.Combine(_testFilesDir, "Photo.png")), "Requires a case-sensitive filesystem.");
        var processor = CreateProcessor();
        var first = await WithPageContextAsync("/first/", () => processor.ProcessBodyAsync(
            Path.Combine(_testFilesDir, "first.md"), "![First](photo.png)"));
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "Photo.png"), 64, 64);
        var second = await WithPageContextAsync("/second/", () => processor.ProcessBodyAsync(
            Path.Combine(_testFilesDir, "second.md"), "![Second](Photo.png)"));
        Assert.NotEqual(ImageSource(first), ImageSource(second));
        Assert.Contains("width=\"64\"", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidImagePath_ReportsMarkdownAndReference()
    {
        var markdown = CreateMarkdownFile("invalid-path.md", "![Invalid](a%00.png)");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPageContextAsync("/invalid/", () => ProcessFileAsync(CreateProcessor(), markdown)));
        Assert.Contains("a%00.png", exception.Message, StringComparison.Ordinal);
        Assert.Contains(markdown, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnpairedSurrogateName_CanBeRenderedOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "NTFS supports unpaired UTF-16 surrogates.");
        var name = "photo\uD800.png";
        await CreateTestImageAsync(Path.Combine(_testFilesDir, name), 32, 32);
        var processor = CreateProcessor();
        var html = await WithPageContextAsync("/surrogate/", () => processor.ProcessBodyAsync(
            Path.Combine(_testFilesDir, "surrogate.md"), $"![Photo]({name.ToUpperInvariant()})"));
        Assert.Contains("photo%25d800.png.", html, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_outputDir, "*.webp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnicodeNormalizationAlias_CanResolveAnExistingImage()
    {
        var diskName = "cafe\u0301.png";
        var alias = "caf\u00e9.png";
        await CreateTestImageAsync(Path.Combine(_testFilesDir, diskName), 32, 32);
        Assert.SkipUnless(File.Exists(Path.Combine(_testFilesDir, alias)), "Requires a normalization-insensitive filesystem.");
        var markdown = CreateMarkdownFile("unicode.md", $"![Photo]({alias})");
        var html = await WithPageContextAsync("/unicode/", () => ProcessFileAsync(CreateProcessor(), markdown));
        Assert.Contains("caf%C3%A9.png.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortFilenameAlias_CanResolveAnExistingImage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "8.3 aliases require Windows.");
        var path = Path.Combine(_testFilesDir, "Photograph with a long name.png");
        await CreateTestImageAsync(path, 32, 32);
        using var command = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c for %I in (\"{path}\") do @echo %~sI",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var shortPath = (await command.StandardOutput.ReadToEndAsync()).Trim();
        await command.WaitForExitAsync();
        var shortName = Path.GetFileName(shortPath);
        Assert.SkipUnless(shortName.Contains('~') && File.Exists(shortPath), "8.3 names are disabled on this volume.");
        var markdown = CreateMarkdownFile("short.md", $"![Photo]({shortName})");
        var html = await WithPageContextAsync("/short/", () => ProcessFileAsync(CreateProcessor(), markdown));
        Assert.StartsWith("/_images/", ImageSource(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ColonInRelativeSourceName_IsSupportedOnUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Colons are not ordinary Windows filename characters.");
        await CreateTestImageAsync(Path.Combine(_testFilesDir, "photo:one.png"), 32, 32);
        var markdown = CreateMarkdownFile("colon.md", "![Photo](./photo:one.png)");
        var html = await WithPageContextAsync("/colon/", () => ProcessFileAsync(CreateProcessor(), markdown));
        Assert.Contains("photo%253Aone.png.", html, StringComparison.Ordinal);
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
        }, new SiteExecutionPaths(_testDir), site ?? TestArticleContents.CreateSiteInfo(), new ContentPath([_testFilesDir]), imageProcessor ?? new ImageProcessor());
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
            Dependencies = tracked ? new BuildDependencyRecorder() : null,
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
