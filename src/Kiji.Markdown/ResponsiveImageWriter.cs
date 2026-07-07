using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;
using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document state for responsive image rendering: the processed image lookup,
/// the content-scoped asset location, and the image counter driving eager/lazy loading.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string assetsBaseUrl,
    string contentKey)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public string AssetsBaseUrl { get; } = $"/{assetsBaseUrl.Trim('/')}";

    public string ContentKey { get; } = contentKey;

    public int ImageCount;
}

/// <summary>
/// Renders local markdown images as responsive images with lazy loading and CLS optimization.
/// Attached per renderer (not per pipeline) because its state is per document.
/// </summary>
internal static class ResponsiveImageWriter
{
    public static void Attach(HtmlRenderer renderer, ResponsiveImageContext context)
    {
        var linkRenderer = renderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
        linkRenderer?.TryWriters.Add((r, link) => TryWriteResponsiveImage(r, link, context));
    }

    private static bool TryWriteResponsiveImage(HtmlRenderer renderer, LinkInline link, ResponsiveImageContext context)
    {
        // Only handle images
        if (!link.IsImage)
        {
            return false;
        }

        var url = link.Url ?? string.Empty;
        var alt = GetAltText(link);
        var title = link.Title ?? string.Empty;

        if (LocalImageUrl.IsLocalImage(url))
        {
            WriteResponsiveImage(renderer, context, url, alt, title);
            return true;
        }

        return false;
    }

    private static void WriteResponsiveImage(HtmlRenderer renderer, ResponsiveImageContext context, string url, string alt, string title)
    {
        var imageReferenceKey = ImageReferenceKey.FromMarkdownUrl(url);

        if (!context.ImageInfoLookup.TryGetValue(imageReferenceKey, out var imageInfo))
        {
            // Fallback: if no image info found, render as simple img
            WriteSimpleImage(renderer, context, url, alt, title);
            return;
        }

        // Image path: {assetsBaseUrl}/{contentKey}/{filename}.{hash}.{width}w.webp
        var availableWidths = imageInfo.AvailableWidths;
        var largestWidth = availableWidths.Length > 0 ? availableWidths.Max() : imageInfo.OriginalWidth;
        var defaultImageUrl = $"{context.AssetsBaseUrl}/{context.ContentKey}/{imageInfo.AssetFileNameBase}.{imageInfo.ContentHash}.{largestWidth}w.webp";

        // Generate srcset with all available widths
        var srcsetParts = availableWidths.Select(w =>
            $"{context.AssetsBaseUrl}/{context.ContentKey}/{imageInfo.AssetFileNameBase}.{imageInfo.ContentHash}.{w}w.webp {w}w"
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
        var loading = context.ImageCount == 0 ? "eager" : "lazy";
        renderer.Write($"\" loading=\"{loading}\" decoding=\"async\" class=\"blog-image\"");
        context.ImageCount++;

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

    private static void WriteSimpleImage(HtmlRenderer renderer, ResponsiveImageContext context, string url, string alt, string title)
    {
        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(url);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        var loading = context.ImageCount == 0 ? "eager" : "lazy";
        renderer.Write($"\" loading=\"{loading}\" decoding=\"async\" class=\"blog-image\"");
        context.ImageCount++;

        if (!string.IsNullOrEmpty(title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(title);
            renderer.Write("\"");
        }

        renderer.Write(">");
    }

    private static string GetAltText(LinkInline link)
    {
        if (link.FirstChild is LiteralInline literal)
        {
            return literal.Content.ToString();
        }

        return string.Empty;
    }
}
