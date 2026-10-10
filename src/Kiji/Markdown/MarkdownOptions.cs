using Markdig;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>Configures Markdown file selection and rendering.</summary>
public sealed class MarkdownOptions
{
    internal List<Action<MarkdownPipelineBuilder>> PipelineConfigurations { get; } = [];

    internal List<Func<string, string>> HtmlPostProcessors { get; } = [];

    internal List<Action<DeserializerBuilder>> FrontMatterConfigurations { get; } = [];

    /// <summary>The directory to scan recursively for <c>*.md</c>. Absolute or relative to the site project directory; defaults to <c>contents</c>.</summary>
    public string Directory { get; set; } = "contents";

    /// <summary>An optional file filter applied before loading, defaulting to all Markdown files.</summary>
    public Func<FileInfo, bool>? FileFilter { get; set; }

    /// <summary>Registers a Markdig configuration applied after the default pipeline is configured.</summary>
    public void ConfigureMarkdown(Action<MarkdownPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        PipelineConfigurations.Add(configure);
    }

    /// <summary>Registers an HTML post-processor applied after Markdown rendering, in registration order.</summary>
    public void AddHtmlPostProcessor(Func<string, string> postProcessor)
    {
        ArgumentNullException.ThrowIfNull(postProcessor);
        HtmlPostProcessors.Add(postProcessor);
    }

    /// <summary>Registers a YAML deserializer configuration after the camelCase and ignore-unmatched-properties defaults.</summary>
    public void ConfigureYaml(Action<DeserializerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        FrontMatterConfigurations.Add(configure);
    }
}
