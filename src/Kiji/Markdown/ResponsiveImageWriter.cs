using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Kiji.Markdown;

/// <summary>
/// Renders local markdown images as responsive images with lazy loading and CLS
/// optimization. Variants live in the page's own output directory, so URLs are
/// <c>./</c>-relative. Attached per renderer (not per pipeline) because its state
/// is per document.
/// </summary>
internal static class ResponsiveImageWriter
{
    public static void Attach(HtmlRenderer renderer, ResponsiveImageContextHolder contextHolder)
    {
        var linkRenderer = renderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
        linkRenderer?.TryWriters.Add((r, link) => TryWriteResponsiveImage(r, link, contextHolder.Current));
    }

    private static bool TryWriteResponsiveImage(HtmlRenderer renderer, LinkInline link, ResponsiveImageContext? context)
    {
        // Only handle images rendered with a current context (i.e. through the
        // markdown processor); otherwise fall back to Markdig's default writer.
        if (context is null || !link.IsImage)
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
        var referenceKey = ImageReferenceKey.FromMarkdownUrl(url);

        if (!context.ImageInfoLookup.TryGetValue(referenceKey, out var imageInfo) || imageInfo.Variants.Count == 0)
        {
            // Fallback: if no image info found, render as simple img
            WriteSimpleImage(renderer, context, url, alt, title);
            return;
        }

        // Variants sit beside the page output, mirroring the reference's directory part.
        var directory = GetDirectoryPrefix(referenceKey);
        var variants = imageInfo.Variants;
        var largest = variants[^1];

        var srcset = string.Join(", ", variants.Select(variant => $"{CreateVariantUrl(directory, variant.FileName)} {variant.Width}w"));
        var sizes = $"(max-width: {largest.Width}px) 100vw, {largest.Width}px";

        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(CreateVariantUrl(directory, largest.FileName));
        renderer.Write("\" srcset=\"");
        renderer.WriteEscape(srcset);
        renderer.Write("\" sizes=\"");
        renderer.WriteEscape(sizes);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        renderer.Write("\"");
        WriteCommonImageAttributes(renderer, context);

        // Add actual image dimensions based on original aspect ratio
        renderer.Write($" width=\"{imageInfo.OriginalWidth}\" height=\"{imageInfo.OriginalHeight}\"");

        WriteTitleAttribute(renderer, title);
        renderer.Write(">");
    }

    private static void WriteSimpleImage(HtmlRenderer renderer, ResponsiveImageContext context, string url, string alt, string title)
    {
        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(url);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        renderer.Write("\"");
        WriteCommonImageAttributes(renderer, context);
        WriteTitleAttribute(renderer, title);
        renderer.Write(">");
    }

    private static string GetDirectoryPrefix(string referenceKey)
    {
        var separatorIndex = referenceKey.LastIndexOf('/');
        return separatorIndex >= 0 ? referenceKey[..(separatorIndex + 1)] : string.Empty;
    }

    private static string CreateVariantUrl(string directory, string fileName)
    {
        // srcset uses spaces and commas as syntax, so HTML escaping alone is not
        // enough. Encode each path segment while preserving directory separators.
        return "./" + string.Join('/', (directory + fileName).Split('/').Select(Uri.EscapeDataString));
    }

    private static void WriteCommonImageAttributes(HtmlRenderer renderer, ResponsiveImageContext context)
    {
        // The first image is above the fold more often than not; load it eagerly.
        var loading = context.ImageCount == 0 ? "eager" : "lazy";
        renderer.Write($" loading=\"{loading}\" decoding=\"async\"");
        context.ImageCount++;

    }

    private static void WriteTitleAttribute(HtmlRenderer renderer, string title)
    {
        if (!string.IsNullOrEmpty(title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(title);
            renderer.Write("\"");
        }
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
