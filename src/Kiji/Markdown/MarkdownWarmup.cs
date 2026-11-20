using System.Diagnostics.CodeAnalysis;
using Kiji.Assets;
using Kiji.Generation;

namespace Kiji.Markdown;

/// <summary>
/// Prepares the Markdown and YAML code on a spare processor while the site is still being
/// configured. A process uses that code for the first time when it loads content, and the
/// first use costs far more than the work itself: the runtime loads, compiles and initializes
/// it then. Content loading and rendering wait on one thread for that, with the others idle.
/// </summary>
/// <remarks>
/// The sample runs the paths every Markdown file takes, with the default options. It reads
/// nothing, writes nothing and creates no site state, so it cannot affect the output.
/// </remarks>
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

    /// <summary>Starts the warm-up once per process. Returns immediately.</summary>
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

    /// <summary>Runs the sample through front matter parsing, Markdown parsing and rendering.</summary>
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
