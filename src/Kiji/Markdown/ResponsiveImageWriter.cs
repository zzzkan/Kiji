using System.Globalization;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Kiji.Markdown;

/// <summary>
/// Renders local markdown images as responsive images with lazy loading and CLS
/// optimization. Variants live in the page's own output directory, so URLs are
/// rooted at that directory beneath the site's deployment base path. Attached per
/// renderer (not per pipeline) because its state is per document.
/// </summary>
internal sealed class ResponsiveImageWriter
{
    internal ResponsiveImageContext? Context { get; set; }

    internal ResponsiveImageWriter(HtmlRenderer renderer)
    {
        var linkRenderer = renderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
        linkRenderer?.TryWriters.Add((r, link) => TryWriteResponsiveImage(r, link, Context));
    }

    private static bool TryWriteResponsiveImage(HtmlRenderer renderer, LinkInline link, ResponsiveImageContext? context)
    {
        // Only handle images rendered with a current context (i.e. through the
        // markdown processor); otherwise fall back to Markdig's default writer.
        if (context is null || !link.IsImage || !renderer.EnableHtmlForInline)
        {
            return false;
        }

        var url = link.Url ?? string.Empty;
        var title = link.Title ?? string.Empty;

        if (LocalImageUrl.IsLocalImage(url))
        {
            WriteResponsiveImage(renderer, context, link, url, title);
            return true;
        }

        return false;
    }

    private static void WriteResponsiveImage(HtmlRenderer renderer, ResponsiveImageContext context, LinkInline link, string url, string title)
    {
        var isLazy = context.ImageCount++ > 0;
        var referenceKey = ImageReferenceKey.FromMarkdownUrl(url);
        var attributes = link.TryGetAttributes();
        if (TryGetAttribute(attributes, "loading", out var loading))
        {
            isLazy = string.Equals(loading, "lazy", StringComparison.OrdinalIgnoreCase);
        }

        renderer.Write("<img");
        if (context.ImageInfoLookup.TryGetValue(referenceKey, out var imageInfo) && imageInfo.Variants.Count > 0)
        {
            // Variants sit beside the page output, mirroring the reference's directory part.
            var directory = GetDirectoryPrefix(referenceKey);
            var publicDirectory = context.OutputUrlDirectory + EncodeRelativePath(directory);
            var variants = imageInfo.Variants;
            var largest = variants[^1];

            var srcset = string.Join(", ", variants.Select(variant => $"{CreateVariantUrl(publicDirectory, variant.FileName)} {variant.Width}w"));
            // Auto sizes requires lazy loading, including an explicit loading override.
            var sizes = $"(max-width: {largest.Width}px) 100vw, {largest.Width}px";
            if (isLazy) { sizes = "auto, " + sizes; }

            WriteAttribute(renderer, attributes, "src", CreateVariantUrl(publicDirectory, largest.FileName), escapeUrl: true);
            WriteAttribute(renderer, attributes, "srcset", srcset);
            WriteAttribute(renderer, attributes, "sizes", sizes);
            WriteAttribute(renderer, attributes, "width", GetDimension(attributes, "height", imageInfo.OriginalWidth, imageInfo.OriginalHeight));
            WriteAttribute(renderer, attributes, "height", GetDimension(attributes, "width", imageInfo.OriginalHeight, imageInfo.OriginalWidth));
        }
        else
        {
            // Processing may have been skipped upstream.
            WriteAttribute(renderer, attributes, "src", url, escapeUrl: true);
        }

        if (!TryGetAttribute(attributes, "alt", out _))
        {
            renderer.Write(" alt=\"");
            WriteAltText(renderer, link);
            renderer.Write('"');
        }
        WriteAttribute(renderer, attributes, "loading", isLazy ? "lazy" : "eager");
        WriteAttribute(renderer, attributes, "decoding", "async");
        if (!string.IsNullOrEmpty(title)) { WriteAttribute(renderer, attributes, "title", title); }
        renderer.WriteAttributes(link);
        renderer.Write(">");
    }

    private static string GetDirectoryPrefix(string referenceKey)
    {
        var separatorIndex = referenceKey.LastIndexOf('/');
        return separatorIndex >= 0 ? referenceKey[..(separatorIndex + 1)] : string.Empty;
    }

    private static string CreateVariantUrl(string publicDirectory, string fileName)
    {
        // srcset uses spaces and commas as syntax, so HTML escaping alone is not
        // enough. Encode each path segment while preserving directory separators.
        return publicDirectory + EncodeRelativePath(fileName);
    }

    private static string EncodeRelativePath(string path)
    {
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    private static bool TryGetAttribute(HtmlAttributes? attributes, string name, out string? value)
    {
        if (attributes?.Properties is { } properties)
        {
            foreach (var property in properties)
            {
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = null;
        return false;
    }

    private static int ParseDimension(string? value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var dimension) && dimension > 0 ? dimension : 0;

    private static string? GetDimension(HtmlAttributes? attributes, string otherName, int original, int otherOriginal)
    {
        if (!TryGetAttribute(attributes, otherName, out var value)) { return original.ToString(CultureInfo.InvariantCulture); }
        var other = ParseDimension(value);
        if (other == 0) { return null; }

        // Round to the nearest pixel, keeping small positive dimensions visible.
        return Math.Max(1, ((long)original * other + otherOriginal / 2) / otherOriginal).ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteAttribute(HtmlRenderer renderer, HtmlAttributes? attributes, string name, string? value, bool escapeUrl = false)
    {
        if (value is null || TryGetAttribute(attributes, name, out _)) { return; }
        renderer.Write(' ').Write(name).Write("=\"");
        if (escapeUrl) { renderer.WriteEscapeUrl(value); }
        else { renderer.WriteEscape(value); }
        renderer.Write('"');
    }

    private static void WriteAltText(HtmlRenderer renderer, LinkInline link)
    {
        var enabled = renderer.EnableHtmlForInline;
        renderer.EnableHtmlForInline = false;
        try
        {
            renderer.WriteChildren(link);
        }
        finally { renderer.EnableHtmlForInline = enabled; }
    }
}
