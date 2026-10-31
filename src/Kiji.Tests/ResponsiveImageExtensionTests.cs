using Markdig;
using Xunit;
using Kiji.Markdown;
using Kiji.Assets;
using System.Text.RegularExpressions;

namespace Kiji.Tests;

public sealed class ResponsiveImageExtensionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Process_LocalImage_PreservesAttributesAndCompleteAltText(bool processed)
    {
        var images = new Dictionary<string, ProcessedImageInfo>();
        if (processed) { images["photo.png"] = CreateImageInfo("photo.png"); }
        var html = Render("![Before **bold** and `code` & after](photo.png){#photo .rounded data-caption=example}", images);
        Assert.Contains("alt=\"Before bold and code &amp; after\"", html);
        Assert.Contains("id=\"photo\"", html);
        Assert.Contains("class=\"rounded\"", html);
        Assert.Contains("data-caption=\"example\"", html);
    }

    [Fact]
    public void Process_ExplicitAttributesOverrideGeneratedAttributesWithoutDuplicates()
    {
        var images = new Dictionary<string, ProcessedImageInfo> { ["photo.png"] = CreateImageInfo("photo.png") };

        var values = ReadImageAttributes(Render("![Photo](photo.png){width=300 height=200 loading=lazy}", images));

        Assert.Equal("300", values["width"]);
        Assert.Equal("200", values["height"]);
        Assert.Equal("lazy", values["loading"]);
    }

    [Theory]
    [InlineData("lazy", "auto, ")]
    [InlineData("eager", "")]
    public void Process_ExplicitLoadingControlsAutomaticSizes(string loading, string sizesPrefix)
    {
        var images = new Dictionary<string, ProcessedImageInfo> { ["photo.png"] = CreateImageInfo("photo.png") };
        var markdown = $"![Photo](photo.png){{loading={loading}}}";

        var html = Render(markdown + "\n\n" + markdown, images);

        Assert.Equal(2, Regex.Count(html, $"sizes=\"{Regex.Escape(sizesPrefix)}\\(max-width: 1920px\\) 100vw, 1920px\""));
    }

    [Theory]
    [InlineData("width=300", "300", "169")]
    [InlineData("height=300", "533", "300")]
    [InlineData("width=1", "1", "1")]
    public void Process_OneExplicitDimensionPreservesAspectRatio(string attributes, string width, string height)
    {
        var images = new Dictionary<string, ProcessedImageInfo> { ["photo.png"] = CreateImageInfo("photo.png") };

        var values = ReadImageAttributes(Render($"![Photo](photo.png){{{attributes}}}", images));

        Assert.Equal(width, values["width"]);
        Assert.Equal(height, values["height"]);
    }

    [Theory]
    [InlineData("width", "height", "50%")]
    [InlineData("height", "width", "auto")]
    public void Process_NonIntegerDimensionOmitsTheOtherDimension(string name, string otherName, string value)
    {
        var images = new Dictionary<string, ProcessedImageInfo> { ["photo.png"] = CreateImageInfo("photo.png") };

        var values = ReadImageAttributes(Render($"![Photo](photo.png){{{name}={value}}}", images));

        Assert.Equal(value, values[name]);
        Assert.False(values.ContainsKey(otherName));
    }

    [Fact]
    public void Process_UnprocessedImageDoesNotInventMissingDimensions()
    {
        var values = ReadImageAttributes(Render("![Photo](photo.png){width=300}", new Dictionary<string, ProcessedImageInfo>()));

        Assert.Equal("300", values["width"]);
        Assert.False(values.ContainsKey("height"));
    }

    private static Dictionary<string, string> ReadImageAttributes(string html)
    {
        var tag = Regex.Match(html, "<img[^>]*>").Value;
        Assert.NotEmpty(tag);
        // A duplicate attribute, including a case variant, fails ToDictionary.
        return Regex.Matches(tag, "([a-zA-Z]+)=\"([^\"]*)\"")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static string Render(
        string markdown,
        IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
        string outputUrlDirectory = "/kiji/blog/test-post/")
    {
        var document = global::Markdig.Markdown.Parse(markdown, Pipeline);
        return PooledMarkdigRenderer.Create(Pipeline).Render(
            document, new ResponsiveImageContext(imageInfoLookup, outputUrlDirectory));
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
    public void Process_LocalImage_GeneratesRootRelativeSrcAndSrcset()
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>
        {
            ["test-image.png"] = CreateImageInfo("test-image.png"),
        };
        var markdown = "![Alt text](test-image.png \"Image title\")";

        var html = Render(markdown, imageInfoLookup);

        Assert.Contains("src=\"/kiji/blog/test-post/test-image.png.abc12345.1920w.webp\"", html);
        Assert.Contains("srcset=\"", html);
        Assert.Contains("/kiji/blog/test-post/test-image.png.abc12345.320w.webp 320w", html);
        Assert.Contains("/kiji/blog/test-post/test-image.png.abc12345.1920w.webp 1920w", html);
        Assert.Contains("sizes=\"(max-width: 1920px) 100vw, 1920px\"", html);
        Assert.Contains("width=\"1920\"", html);
        Assert.Contains("height=\"1080\"", html);
        Assert.Contains("alt=\"Alt text\"", html);
        Assert.Contains("title=\"Image title\"", html);
        Assert.Contains("loading=\"eager\"", html);
        Assert.Contains("decoding=\"async\"", html);
        Assert.DoesNotContain("class=", html);
        Assert.DoesNotContain("src=\"https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_LocalImages_FirstIsEagerAndSubsequentUseAutomaticSizes()
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
        Assert.Contains("sizes=\"(max-width: 1920px) 100vw, 1920px\"", images[0], StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", images[1], StringComparison.Ordinal);
        Assert.Contains("sizes=\"auto, (max-width: 1920px) 100vw, 1920px\"", images[1], StringComparison.Ordinal);
        Assert.All(images, image => Assert.Contains("decoding=\"async\"", image, StringComparison.Ordinal));
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

        Assert.Contains("/kiji/blog/test-post/foo.jpg.jpg12345.", html);
        Assert.Contains("/kiji/blog/test-post/foo.png.png67890.", html);
    }

}
