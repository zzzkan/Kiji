using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Kiji.Markdown;

/// <summary>
/// Custom Markdig extension for secure external links.
/// Adds target="_blank" and rel="noopener noreferrer" to external links.
/// </summary>
/// <remarks>
/// Handles:
/// - [text](url) syntax via LinkInlineRenderer.TryWriters
/// - &lt;URL&gt; syntax via AutolinkInlineParser.Options and AutolinkInlineRenderer.Rel
/// - bare URLs (https://...) via AutoLinkParser.Options
/// </remarks>
public sealed class SecureLinkExtension : IMarkdownExtension
{
    /// <inheritdoc/>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Configure AutolinkInlineParser for <URL> syntax to open in new window
        var autolinkInlineParser = pipeline.InlineParsers.FindExact<AutolinkInlineParser>();
        if (autolinkInlineParser is not null)
        {
            autolinkInlineParser.Options.OpenInNewWindow = true;
        }

        // Configure AutoLinkParser for bare URLs (https://..., www.) to open in new window
        var autoLinkParser = pipeline.InlineParsers.FindExact<AutoLinkParser>();
        if (autoLinkParser is not null)
        {
            autoLinkParser.Options.OpenInNewWindow = true;
        }
    }

    /// <inheritdoc/>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            // Hook into LinkInlineRenderer for [text](url) and bare URL syntax
            var linkRenderer = htmlRenderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
            linkRenderer?.TryWriters.Add(TryWriteSecureLink);

            // Configure AutolinkInlineRenderer for <URL> syntax
            var autolinkRenderer = htmlRenderer.ObjectRenderers.FindExact<AutolinkInlineRenderer>();
            if (autolinkRenderer is not null)
            {
                autolinkRenderer.Rel = "noopener noreferrer";
            }
        }
    }

    private static bool TryWriteSecureLink(HtmlRenderer renderer, LinkInline link)
    {
        // Only handle non-image links (let other extensions handle images)
        if (link.IsImage)
        {
            return false;
        }

        var url = link.Url ?? string.Empty;
        var title = link.Title ?? string.Empty;

        // Only handle external links
        if (!IsExternalUrl(url))
        {
            return false;
        }

        renderer.Write("<a href=\"");
        renderer.WriteEscapeUrl(url);
        renderer.Write("\"");

        if (!string.IsNullOrEmpty(title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(title);
            renderer.Write("\"");
        }

        // Add security attributes for external links
        renderer.Write(" target=\"_blank\" rel=\"noopener noreferrer\"");

        renderer.Write(">");
        renderer.WriteChildren(link);
        renderer.Write("</a>");

        return true;
    }

    private static bool IsExternalUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
