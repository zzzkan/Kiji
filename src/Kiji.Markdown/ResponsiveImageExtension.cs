using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;
using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Custom Markdig extension for responsive images with lazy loading and CLS optimization.
/// </summary>
public sealed class ResponsiveImageExtension(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string assetsBaseUrl,
    string contentKey) : IMarkdownExtension
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private readonly IReadOnlyDictionary<string, ProcessedImageInfo> _imageInfoLookup = imageInfoLookup;
    private readonly string _assetsBaseUrl = $"/{assetsBaseUrl.Trim('/')}";
    private readonly string _contentKey = contentKey;
    private int _imageCount;

    /// <inheritdoc/>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // No setup needed for the pipeline builder
    }

    /// <inheritdoc/>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            var linkRenderer = htmlRenderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
            linkRenderer?.TryWriters.Add(TryWriteResponsiveImage);
        }
    }

    private bool TryWriteResponsiveImage(HtmlRenderer renderer, LinkInline link)
    {
        // Only handle images
        if (!link.IsImage)
        {
            return false;
        }

        var url = link.Url ?? string.Empty;
        var alt = GetAltText(link);
        var title = link.Title ?? string.Empty;

        if (IsLocalImage(url))
        {
            WriteResponsiveImage(renderer, url, alt, title);
            return true;
        }

        return false;
    }

    private void WriteResponsiveImage(HtmlRenderer renderer, string url, string alt, string title)
    {
        var imageReferenceKey = ImageReferenceKey.FromMarkdownUrl(url);
        var imageInfo = GetImageInfo(imageReferenceKey);

        if (imageInfo is null)
        {
            // Fallback: if no image info found, render as simple img
            WriteSimpleImage(renderer, url, alt, title);
            return;
        }

        // Image path: {assetsBaseUrl}/{contentKey}/{filename}.{hash}.{width}w.webp
        var availableWidths = imageInfo.AvailableWidths;
        var largestWidth = availableWidths.Length > 0 ? availableWidths.Max() : imageInfo.OriginalWidth;
        var defaultImageUrl = $"{_assetsBaseUrl}/{_contentKey}/{imageInfo.AssetFileNameBase}.{imageInfo.ContentHash}.{largestWidth}w.webp";

        // Generate srcset with all available widths
        var srcsetParts = availableWidths.Select(w =>
            $"{_assetsBaseUrl}/{_contentKey}/{imageInfo.AssetFileNameBase}.{imageInfo.ContentHash}.{w}w.webp {w}w"
        );
        var srcset = string.Join(", ", srcsetParts);

        // Generate sizes attribute based on maximum width (capped at 960px)
        var maxSizeForSizes = Math.Min(largestWidth, 960);
        var sizes = $"(max-width: {maxSizeForSizes}px) 100vw, {maxSizeForSizes}px";

        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(defaultImageUrl);
        renderer.Write("\" srcset=\"");
        renderer.Write(srcset);
        renderer.Write("\" sizes=\"");
        renderer.Write(sizes);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        var loading = _imageCount == 0 ? "eager" : "lazy";
        renderer.Write($"\" loading=\"{loading}\" decoding=\"async\" class=\"blog-image\"");
        _imageCount++;

        // Add actual image dimensions based on original aspect ratio
        renderer.Write($" width=\"{imageInfo.OriginalWidth}\" height=\"{imageInfo.OriginalHeight}\"");

        if (!string.IsNullOrEmpty(title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(title);
            renderer.Write("\"");
        }

        renderer.Write(">");
    }

    private void WriteSimpleImage(HtmlRenderer renderer, string url, string alt, string title)
    {
        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(url);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        var loading = _imageCount == 0 ? "eager" : "lazy";
        renderer.Write($"\" loading=\"{loading}\" decoding=\"async\" class=\"blog-image\"");
        _imageCount++;

        if (!string.IsNullOrEmpty(title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(title);
            renderer.Write("\"");
        }

        renderer.Write(">");
    }

    private ProcessedImageInfo? GetImageInfo(string imageReferenceKey)
    {
        return _imageInfoLookup.TryGetValue(imageReferenceKey, out var info)
            ? info
            : null;
    }

    private static string GetAltText(LinkInline link)
    {
        if (link.FirstChild is LiteralInline literal)
        {
            return literal.Content.ToString();
        }

        return string.Empty;
    }

    private static bool IsLocalImage(string url)
    {
        // Local images are relative paths with image extensions
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(url).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }
}
