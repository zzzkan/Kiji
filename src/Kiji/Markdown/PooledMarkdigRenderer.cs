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
    private readonly ResponsiveImageWriter _imageWriter;

    private PooledMarkdigRenderer(StringWriter writer, HtmlRenderer renderer)
    {
        _writer = writer;
        _renderer = renderer;
        _imageWriter = new ResponsiveImageWriter(renderer);
    }

    internal static PooledMarkdigRenderer Create(MarkdownPipeline pipeline)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);

        return new PooledMarkdigRenderer(writer, renderer);
    }

    internal string Render(MarkdownDocument document, ResponsiveImageContext imageContext)
    {
        var builder = _writer.GetStringBuilder();
        builder.Clear();
        _imageWriter.Context = imageContext;
        try
        {
            _renderer.Render(document);
            _writer.Flush();
            return builder.ToString();
        }
        finally
        {
            _imageWriter.Context = null;
        }
    }
}
