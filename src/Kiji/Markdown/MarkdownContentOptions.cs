using Markdig;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Configures a markdown content source: which files it reads, how its items are
/// ordered and validated, the Markdig pipeline, transforms over the rendered HTML, and
/// front matter deserialization. Passed to
/// <see cref="KijiBuilderExtensions.AddMarkdownContent{TFrontMatter}"/>.
/// </summary>
public sealed class MarkdownContentOptions<TModel> : ContentSourceOptions<TModel>
    where TModel : class
{
    internal MarkdownProcessingOptions Processing { get; } = new();

    internal List<Action<DeserializerBuilder>> FrontMatterConfigurations { get; } = [];

    /// <summary>
    /// The directory this source reads, relative to the content directory. Every
    /// <c>*.md</c> beneath it is discovered recursively. Default: the content directory
    /// itself. Set it to keep markdown with different front matter in separate
    /// collections, e.g. <c>"posts"</c> for <c>contents/posts/</c>.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Optional filter over the discovered files; only those it accepts are loaded.
    /// Default: every file.
    /// </summary>
    public Func<MarkdownFileInfo, bool>? Where { get; set; }

    /// <summary>
    /// Optional CSS class applied to images rendered from markdown. Default: none.
    /// </summary>
    public string? ImageCssClass
    {
        get => Processing.ImageCssClass;
        set => Processing.ImageCssClass = value;
    }

    /// <inheritdoc cref="MarkdownProcessingOptions.ConfigurePipeline"/>
    public void ConfigurePipeline(Action<MarkdownPipelineBuilder> configure)
    {
        Processing.ConfigurePipeline(configure);
    }

    /// <inheritdoc cref="MarkdownProcessingOptions.AddHtmlPostProcessor"/>
    public void AddHtmlPostProcessor(Func<string, string> postProcessor)
    {
        Processing.AddHtmlPostProcessor(postProcessor);
    }

    /// <summary>
    /// Configures the YAML deserializer used for front matter. Runs after the built-in
    /// defaults (camelCase naming convention, unmatched properties ignored).
    /// </summary>
    public void ConfigureFrontMatter(Action<DeserializerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        FrontMatterConfigurations.Add(configure);
    }

    /// <summary>
    /// Resolves <see cref="Directory"/> against the content directory, rejecting a path
    /// that would escape it.
    /// </summary>
    internal string ResolveContentsDirectory(string contentsPath)
    {
        if (string.IsNullOrWhiteSpace(Directory))
        {
            return contentsPath;
        }

        var resolved = Path.GetFullPath(Path.Combine(contentsPath, Directory));
        if (!resolved.StartsWith(contentsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolved, contentsPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Markdown content directory '{Directory}' resolves outside the content directory '{contentsPath}'.");
        }

        return resolved;
    }

    /// <summary>
    /// <see cref="Directory"/> normalized into a content-set scope key: forward slashes,
    /// no leading or trailing separator, empty for the whole content directory. The key
    /// lands in the build manifest, so it must not depend on the host's separator.
    /// </summary>
    internal string ResolveContentSetScope()
    {
        return string.IsNullOrWhiteSpace(Directory)
            ? string.Empty
            : Directory.Replace('\\', '/').Trim('/');
    }
}
