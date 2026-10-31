using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;

namespace Kiji.Markdown;

// Remains replaceable/removable as Markdig's public EmphasisExtraExtension.
internal sealed class GfmStrikethroughExtension() : EmphasisExtraExtension(EmphasisExtraOptions.Strikethrough), IMarkdownExtension
{
    void IMarkdownExtension.Setup(MarkdownPipelineBuilder pipeline)
    {
        Setup(pipeline);
        var parser = pipeline.InlineParsers.Find<EmphasisInlineParser>();
        if (parser is null) { return; }
        var index = parser.EmphasisDescriptors.FindIndex(static descriptor => descriptor.Character == '~');
        if (index >= 0) { parser.EmphasisDescriptors[index] = new EmphasisDescriptor('~', 1, 2, true); }
    }

    void IMarkdownExtension.Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        Setup(pipeline, renderer);
        if (renderer is HtmlRenderer html && html.ObjectRenderers.Find<EmphasisInlineRenderer>() is { } emphasis)
        {
            var previous = emphasis.GetTag;
            emphasis.GetTag = inline => inline.DelimiterChar == '~' ? "del" : previous(inline);
        }
    }
}
