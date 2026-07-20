using Markdig;
using Markdig.Renderers;
using Xunit;
using Kiji.Markdown;
using Kiji.Assets;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="ResponsiveImageWriter"/>.
/// </summary>
public sealed class ResponsiveImageExtensionTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static string Render(
        string markdown,
        IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
        string? imageCssClass = null)
    {
        var document = global::Markdig.Markdown.Parse(markdown, Pipeline);
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        var contextHolder = new ResponsiveImageContextHolder
        {
            Current = new ResponsiveImageContext(imageInfoLookup, imageCssClass),
        };
        ResponsiveImageWriter.Attach(renderer, contextHolder);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static readonly int[] DefaultTargetWidths = [320, 640, 960, 1280];

    private static ProcessedImageInfo CreateImageInfo(
        string fileName,
        int width = 1920,
        int height = 1080,
        string hash = "abc12345")
    {
        int[] widths = [.. DefaultTargetWidths.Where(w => w < width), Math.Min(width, 1920)];

        return new ProcessedImageInfo
        {
            OriginalWidth = width,
            OriginalHeight = height,
            Variants = [.. widths.Select(w => new ImageVariant($"{fileName}.{hash}.{w}w.webp", w))],
        };
    }

    [Fact]
    public void Process_LocalImage_GeneratesRelativeSrcAndSrcset()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"./test-image.png.abc12345.1920w.webp\"", html);
        Assert.Contains("srcset=\"", html);
        Assert.Contains("./test-image.png.abc12345.320w.webp 320w", html);
        Assert.Contains("./test-image.png.abc12345.1280w.webp 1280w", html);
        // No absolute URLs: page-bundle assets sit beside the page output.
        Assert.DoesNotContain("src=\"/", html);
    }

    [Fact]
    public void Process_LocalImageInSubdirectory_KeepsRelativeDirectory()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["images/photo.png"] = CreateImageInfo("photo.png", 640, 480),
        };
        var markdown = "![Alt text](./images/photo.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"./images/photo.png.abc12345.640w.webp\"", html);
        Assert.Contains("./images/photo.png.abc12345.320w.webp 320w", html);
    }

    [Fact]
    public void Process_LocalImage_GeneratesSizesFromLargestVariant()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("sizes=\"(max-width: 1920px) 100vw, 1920px\"", html);
    }

    [Fact]
    public void Process_LocalImage_FirstImageUsesEagerLoading()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("decoding=\"async\"", html);
    }

    [Fact]
    public void Process_LocalImage_SecondImageUsesLazyLoading()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["image1.png"] = CreateImageInfo("image1.png"),
            ["image2.png"] = CreateImageInfo("image2.png"),
        };
        var markdown = "![First](image1.png)\n\n![Second](image2.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("loading=\"lazy\"", html);
    }

    [Fact]
    public void Process_SeparateRenders_EachDocumentStartsWithEagerLoading()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["image1.png"] = CreateImageInfo("image1.png"),
        };

        // Two renders over the same shared pipeline must not leak the image counter.
        var first = Render("![First](image1.png)", imageInfoLookup);
        var second = Render("![First](image1.png)", imageInfoLookup);

        Assert.Contains("loading=\"eager\"", first);
        Assert.Contains("loading=\"eager\"", second);
        Assert.DoesNotContain("loading=\"lazy\"", second);
    }

    [Fact]
    public void Process_LocalImage_IncludesDimensions()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png", 1920, 1080),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("width=\"1920\"", html);
        Assert.Contains("height=\"1080\"", html);
    }

    [Fact]
    public void Process_LocalImage_PreservesAltText()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![My beautiful image](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("alt=\"My beautiful image\"", html);
    }

    [Fact]
    public void Process_LocalImage_PreservesTitle()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png \"Image title\")";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("title=\"Image title\"", html);
    }

    [Fact]
    public void Process_LocalImage_HasNoCssClassByDefault()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.DoesNotContain("class=", html);
    }

    [Fact]
    public void Process_LocalImage_AppliesConfiguredCssClass()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png)";

        var html = Render(markdown, imageInfoLookup, imageCssClass: "post-image");

        Assert.Contains("class=\"post-image\"", html);
    }

    [Fact]
    public void Process_ExternalImage_NotProcessed()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var markdown = "![External image](https://example.com/image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"https://example.com/image.png\"", html);
        Assert.DoesNotContain("srcset", html);
    }

    [Fact]
    public void Process_SiteRootImage_NotProcessed()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var markdown = "![Static image](/icons/logo.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"/icons/logo.png\"", html);
        Assert.DoesNotContain("srcset", html);
    }

    [Fact]
    public void Process_UnknownLocalImage_FallsBackToSimpleImage()
    {
        // No image info provided (only reachable when processing was skipped upstream).
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var markdown = "![Unknown](unknown-image.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"unknown-image.png\"", html);
        Assert.DoesNotContain("srcset", html);
        Assert.Contains("loading=\"eager\"", html);
    }

    [Fact]
    public void Process_LocalImage_SameStemDifferentExtension_UsesReferencedExtension()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["foo.jpg"] = CreateImageInfo("foo.jpg", 900, 450, hash: "jpg12345"),
            ["foo.png"] = CreateImageInfo("foo.png", 400, 200, hash: "png67890"),
        };
        var markdown = "![Jpg](foo.jpg)\n\n![Png](foo.png)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("./foo.jpg.jpg12345.", html);
        Assert.Contains("./foo.png.png67890.", html);
    }

    [Fact]
    public void Process_NonImageLink_NotProcessed()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>();
        var markdown = "[Link text](https://example.com)";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("<a href=\"https://example.com\"", html);
        Assert.DoesNotContain("<img", html);
    }
}
