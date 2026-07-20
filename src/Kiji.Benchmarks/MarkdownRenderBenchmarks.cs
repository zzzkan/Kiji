using BenchmarkDotNet.Attributes;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Kiji.Benchmarks;

/// <summary>
/// Decides whether pooling Markdig's <see cref="HtmlRenderer"/> is worth it:
/// compares the current per-body pattern (new StringWriter + new HtmlRenderer +
/// pipeline.Setup per render) against reusing one renderer with a cleared
/// StringBuilder. Adopt pooling in <c>MarkdownProcessor.Render</c> only if this
/// shows a meaningful (≥5%) win on the render step.
/// </summary>
[MemoryDiagnoser]
public class MarkdownRenderBenchmarks : IDisposable
{
    private MarkdownPipeline _pipeline = null!;
    private MarkdownDocument _document = null!;
    private StringWriter _reusableWriter = null!;
    private HtmlRenderer _reusableRenderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var body = $"""
            # Synthetic Post 00042

            This is a deterministic post used to measure the markdown render step.

            ## Section one

            Post `post-00042` links to [the home page](/) and to [another post](/blog/post-00000/).
            Some *emphasis*, some **strong text**, and some `inline code` follow.

            - First bullet with a bit of text to fill the line out nicely
            - Second bullet mentioning dotnet and performance
            - Third bullet, because lists usually have at least three items

            ## Section two

            ```csharp
            var builder = KijiApp.CreateBuilder(args);
            await using var app = builder.Build();
            return await app.RunAsync();
            ```

            {string.Join("\n\n", Enumerable.Range(0, 20).Select(static i => $"Paragraph {i} with enough text to make the body realistic for a blog article body."))}

            > A block quote for good measure, citing absolutely nobody.
            """;

        _document = global::Markdig.Markdown.Parse(body, _pipeline);

        _reusableWriter = new StringWriter();
        _reusableRenderer = new HtmlRenderer(_reusableWriter);
        _pipeline.Setup(_reusableRenderer);

        // Sanity: both strategies must produce identical HTML.
        var perCall = RendererPerCall();
        var reused = RendererReused();
        if (!string.Equals(perCall, reused, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pooled renderer output diverges from per-call renderer output.");
        }
    }

    public void Dispose()
    {
        _reusableWriter?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public string RendererPerCall()
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        renderer.Render(_document);
        writer.Flush();
        return writer.ToString();
    }

    [Benchmark]
    public string RendererReused()
    {
        var builder = _reusableWriter.GetStringBuilder();
        builder.Clear();

        _reusableRenderer.Render(_document);
        _reusableWriter.Flush();
        return builder.ToString();
    }
}
