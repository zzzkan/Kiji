using Markdig;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Configures markdown content processing: the Markdig pipeline, transforms over the
/// rendered HTML, and front matter deserialization. Passed to
/// <see cref="KijiBuilderExtensions.AddMarkdownContent{TFrontMatter}"/>.
/// </summary>
public sealed class MarkdownContentOptions
{
    internal List<Action<MarkdownPipelineBuilder>> PipelineConfigurations { get; } = [];

    internal List<Func<string, string>> HtmlPostProcessors { get; } = [];

    internal List<Action<DeserializerBuilder>> FrontMatterConfigurations { get; } = [];

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
    public MarkdownContentOptions ConfigurePipeline(Action<MarkdownPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        PipelineConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Appends a transform applied to the final rendered HTML of each markdown file.
    /// Processors run in registration order, each receiving the previous output.
    /// </summary>
    public MarkdownContentOptions AddHtmlPostProcessor(Func<string, string> postProcessor)
    {
        ArgumentNullException.ThrowIfNull(postProcessor);

        HtmlPostProcessors.Add(postProcessor);
        return this;
    }

    /// <summary>
    /// Configures the YAML deserializer used for front matter. Runs after the built-in
    /// defaults (camelCase naming convention, unmatched properties ignored).
    /// </summary>
    public MarkdownContentOptions ConfigureFrontMatter(Action<DeserializerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        FrontMatterConfigurations.Add(configure);
        return this;
    }
}
