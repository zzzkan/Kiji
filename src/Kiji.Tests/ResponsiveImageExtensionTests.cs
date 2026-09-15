using Markdig;
using Markdig.Renderers;
using Xunit;
using Kiji.Markdown;
using Kiji.Assets;

namespace Kiji.Tests;

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

    private static ProcessedImageInfo CreateImageInfo(
        string fileName,
        int width = 1920,
        int height = 1080,
        string hash = "abc12345")
    {
        int[] widths = [320, width];

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
        var markdown = "![Alt text](test-image.png \"Image title\")";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"./test-image.png.abc12345.1920w.webp\"", html);
        Assert.Contains("srcset=\"", html);
        Assert.Contains("./test-image.png.abc12345.320w.webp 320w", html);
        Assert.Contains("./test-image.png.abc12345.1920w.webp 1920w", html);
        Assert.Contains("sizes=\"(max-width: 1920px) 100vw, 1920px\"", html);
        Assert.Contains("width=\"1920\"", html);
        Assert.Contains("height=\"1080\"", html);
        Assert.Contains("alt=\"Alt text\"", html);
        Assert.Contains("title=\"Image title\"", html);
        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("decoding=\"async\"", html);
        Assert.DoesNotContain("class=", html);
        // No absolute URLs: page-bundle assets sit beside the page output.
        Assert.DoesNotContain("src=\"/", html);
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

        var images = html.Split("<img", StringSplitOptions.None).Skip(1).Select(part => part[..part.IndexOf('>')]).ToArray();
        Assert.Equal(2, images.Length);
        Assert.Contains("loading=\"eager\"", images[0], StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", images[1], StringComparison.Ordinal);
        Assert.All(images, image => Assert.Contains("decoding=\"async\"", image, StringComparison.Ordinal));
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
