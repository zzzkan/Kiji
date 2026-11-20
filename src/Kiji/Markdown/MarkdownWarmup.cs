using System.Diagnostics.CodeAnalysis;
using Kiji.Assets;
using Kiji.Generation;

namespace Kiji.Markdown;

internal static class MarkdownWarmup
{
    private const string Sample = """
        ---
        title: Title
        date: 2024-01-02
        tags:
          - one
          - two
        draft: false
        order: 1
        ---

        # Heading

        A paragraph with *emphasis*, **strong** text, `code`, ~~strikethrough~~, a
        [link](https://example.com/ "title"), <https://example.com/> and a footnote[^1].

        - item
        - [x] task

        1. first
        2. second

        > A quotation.

        ```csharp
        var value = 1;
        ```

        | A | B |
        |---|---|
        | 1 | 2 |

        ![image](https://example.com/image.png){.wide}

        ---

        [^1]: A note.

        """;

    private static int s_started;

    internal static void Start()
    {
        if (Environment.ProcessorCount < 2 || Interlocked.Exchange(ref s_started, 1) != 0)
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "Kiji Markdown warm-up" }.Start();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed warm-up changes only timing; the site reports its own errors.")]
    private static void Run()
    {
        try
        {
            Warm();
        }
        catch (Exception)
        {
        }
    }

    internal static string Warm()
    {
        var options = new MarkdownOptions();
        BuildFingerprint.HashText(Sample);
        var (_, body) = MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, object>>(
            Sample, options.CreateDeserializer());
        var pipeline = MarkdownProcessor.BuildPipeline(options);
        var document = Markdig.Markdown.Parse(body, pipeline);
        return PooledMarkdigRenderer.Create(pipeline).Render(document,
            new ResponsiveImageContext(new Dictionary<string, ProcessedImageInfo>(), "/_images/"));
    }
}
