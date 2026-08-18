using Markdig;

namespace Kiji.Markdown;

/// <summary>
/// How markdown becomes HTML: the Markdig pipeline, transforms over the rendered
/// output, and image presentation. Separate from
/// <see cref="MarkdownContentOptions{TModel}"/>, which declares a content source —
/// this half is what <see cref="MarkdownProcessor"/> needs and says nothing about
/// which files a collection reads or how its items are shaped.
/// </summary>
public sealed class MarkdownProcessingOptions
{
    internal List<Action<MarkdownPipelineBuilder>> PipelineConfigurations { get; } = [];

    internal List<Func<string, string>> HtmlPostProcessors { get; } = [];

    /// <summary>
    /// Optional CSS class applied to images rendered from markdown. Default: none.
    /// </summary>
    public string? ImageCssClass { get; set; }

    /// <summary>
    /// Configures the shared Markdig pipeline. Runs after the built-in defaults
    /// (advanced extensions and <see cref="SecureLinkExtension"/>), so built-ins can be
    /// removed here, e.g.
    /// <c>options.ConfigurePipeline(b => b.Extensions.TryRemove&lt;SecureLinkExtension&gt;())</c>.
    /// </summary>
    public void ConfigurePipeline(Action<MarkdownPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        PipelineConfigurations.Add(configure);
    }

    /// <summary>
    /// Appends a transform applied to the final rendered HTML of each markdown file.
    /// Processors run in registration order, each receiving the previous output.
    /// </summary>
    public void AddHtmlPostProcessor(Func<string, string> postProcessor)
    {
        ArgumentNullException.ThrowIfNull(postProcessor);

        HtmlPostProcessors.Add(postProcessor);
    }
}
