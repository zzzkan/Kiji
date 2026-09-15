using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Kiji.Markdown;

/// <summary>
/// A reusable Markdig <see cref="HtmlRenderer"/> with its writer and image try-writer
/// attached exactly once. Not thread-safe; instances are
/// handed out one at a time by <see cref="MarkdownProcessor"/>'s pool.
/// </summary>
internal sealed class PooledMarkdigRenderer
{
    private readonly StringWriter _writer;
    private readonly HtmlRenderer _renderer;
    private readonly ResponsiveImageContextHolder _contextHolder;

    private PooledMarkdigRenderer(StringWriter writer, HtmlRenderer renderer, ResponsiveImageContextHolder contextHolder)
    {
        _writer = writer;
        _renderer = renderer;
        _contextHolder = contextHolder;
    }

    internal static PooledMarkdigRenderer Create(MarkdownPipeline pipeline)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);

        var contextHolder = new ResponsiveImageContextHolder();
        ResponsiveImageWriter.Attach(renderer, contextHolder);

        return new PooledMarkdigRenderer(writer, renderer, contextHolder);
    }

    internal string Render(MarkdownDocument document, ResponsiveImageContext imageContext)
    {
        var builder = _writer.GetStringBuilder();
        builder.Clear();
        _contextHolder.Current = imageContext;
        try
        {
            _renderer.Render(document);
            _writer.Flush();
            return builder.ToString();
        }
        finally
        {
            _contextHolder.Current = null;
        }
    }
}
