using Kiji.Images;
using Markdig;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Kiji.Markdown;
using Kiji.Assets;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="ResponsiveImageExtension"/>.
/// </summary>
public sealed class ResponsiveImageExtensionTests
{
    private static MarkdownPipeline CreatePipeline(IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup, string postsBaseUrl = "_assets")
    {
        return new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Use(new ResponsiveImageExtension(imageInfoLookup, postsBaseUrl, "test-post"))
            .Build();
    }

    private static ProcessedImageInfo CreateImageInfo(string fileName, int width = 1920, int height = 1080, string hash = "abc12345")
    {
        var assetFileNameBase = Path.GetExtension(fileName).Length == 0
            ? $"{fileName}.png"
            : fileName;

        return new ProcessedImageInfo
        {
            AssetFileNameBase = assetFileNameBase,
            FileName = Path.GetFileNameWithoutExtension(assetFileNameBase),
            OriginalWidth = width,
            OriginalHeight = height,
            AvailableWidths = [320, 640, 960, 1280, width > 1920 ? 1920 : width],
            AspectRatio = (double)width / height,
            ContentHash = hash
        };
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

    #region Responsive Image Rendering Tests

    [Fact]
    public void Process_LocalImage_GeneratesSrcset()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("srcset=\"", html);
        Assert.Contains("320w", html);
        Assert.Contains("640w", html);
        Assert.Contains("960w", html);
        Assert.Contains("1280w", html);
        Assert.Contains("/_assets/test-post/", html);
    }

    [Fact]
    public void Process_LocalImage_GeneratesSizes()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("sizes=\"", html);
        Assert.Contains("100vw", html);
    }

    [Fact]
    public void Process_LocalImage_FirstImageUsesEagerLoading()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("decoding=\"async\"", html);
    }

    [Fact]
    public void Process_LocalImage_SecondImageUsesLazyLoading()
    {
        var imageInfo1 = CreateImageInfo("image1.png");
        var imageInfo2 = CreateImageInfo("image2.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["image1.png"] = imageInfo1,
            ["image2.png"] = imageInfo2
        };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![First](image1.png)\n\n![Second](image2.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("loading=\"lazy\"", html);
    }

    [Fact]
    public void Process_LocalImage_IncludesDimensions()
    {
        var imageInfo = CreateImageInfo("test-image.png", 1920, 1080);
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("width=\"1920\"", html);
        Assert.Contains("height=\"1080\"", html);
    }

    [Fact]
    public void Process_LocalImage_PreservesAltText()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![My beautiful image](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("alt=\"My beautiful image\"", html);
    }

    [Fact]
    public void Process_LocalImage_PreservesTitle()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png \"Image title\")";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("title=\"Image title\"", html);
    }

    [Fact]
    public void Process_LocalImage_AddsBlogImageClass()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("class=\"blog-image\"", html);
    }

    [Fact]
    public void Process_ExternalImage_NotProcessed()
    {
        // Arrange
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![External image](https://example.com/image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("src=\"https://example.com/image.png\"", html);
        Assert.DoesNotContain("srcset", html);
    }

    [Fact]
    public void Process_UnknownLocalImage_FallsBackToSimpleImage()
    {
        // Arrange - no image info provided
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Unknown](unknown-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("src=\"unknown-image.png\"", html);
        Assert.DoesNotContain("srcset", html);
        Assert.Contains("loading=\"eager\"", html);
    }

    [Fact]
    public void Process_LocalImage_UsesCorrectBaseUrl()
    {
        var imageInfo = CreateImageInfo("test-image.png");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup, "custom-posts");

        // Act
        var markdown = "![Alt text](test-image.png)";
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("/custom-posts/test-post/", html);
    }

    [Fact]
    public void Process_LocalImage_UsesContentHash()
    {
        var imageInfo = CreateImageInfo("test-image.png", hash: "xyz78901");
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo> { ["test-image.png"] = imageInfo };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Alt text](test-image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("xyz78901", html);
    }

    [Fact]
    public void Process_LocalImage_PrefersReferencedExtensionWhenStemCollides()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["foo.jpg"] = CreateImageInfo("foo.jpg", hash: "jpg12345"),
            ["foo.png"] = CreateImageInfo("foo.png", hash: "png67890")
        };
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "![Jpg](foo.jpg)\n\n![Png](foo.png)";

        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        var jpgIndex = html.IndexOf("jpg12345", StringComparison.Ordinal);
        var pngIndex = html.IndexOf("png67890", StringComparison.Ordinal);

        Assert.NotEqual(-1, jpgIndex);
        Assert.NotEqual(-1, pngIndex);
        Assert.True(jpgIndex < pngIndex);
        Assert.Contains("/foo.jpg.jpg12345.", html);
        Assert.Contains("/foo.png.png67890.", html);
    }

    [Fact]
    public async Task Process_LocalImage_UsesExtensionAwareAssetNamesFromImageProcessor()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"ResponsiveImageExtensionTests_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testDir, "source");
        var outputDir = Path.Combine(testDir, "output");
        Directory.CreateDirectory(sourceDir);

        try
        {
            await CreateTestImageAsync(Path.Combine(sourceDir, "foo.jpg"), 900, 450);
            await CreateTestImageAsync(Path.Combine(sourceDir, "foo.png"), 400, 200);

            var imageInfoLookup = await new ImageProcessor().ProcessPostImagesAsync(outputDir, sourceDir);
            var pipeline = CreatePipeline(imageInfoLookup);
            var markdown = "![Jpg](foo.jpg)\n\n![Png](foo.png)";

            var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);
            var jpgInfo = imageInfoLookup["foo.jpg"];
            var pngInfo = imageInfoLookup["foo.png"];

            Assert.Contains($"/test-post/foo.jpg.{jpgInfo.ContentHash}.{jpgInfo.AvailableWidths.Max()}w.webp", html);
            Assert.Contains($"/test-post/foo.png.{pngInfo.ContentHash}.{pngInfo.AvailableWidths.Max()}w.webp", html);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Process_NonImageLink_NotProcessed()
    {
        // Arrange
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var pipeline = CreatePipeline(imageInfoLookup);
        var markdown = "[Link text](https://example.com)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("<a href=\"https://example.com\"", html);
        Assert.DoesNotContain("<img", html);
    }

    #endregion
}
