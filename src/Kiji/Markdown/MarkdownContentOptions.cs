using Markdig;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>Configures Markdown file selection, validation, and rendering.</summary>
public sealed class MarkdownContentOptions<TModel> : ContentSourceOptions<TModel>
    where TModel : class
{
    internal MarkdownProcessingOptions Processing { get; } = new();

    internal List<Action<DeserializerBuilder>> FrontMatterConfigurations { get; } = [];

    /// <summary>The directory to scan recursively for <c>*.md</c>, relative to the content directory and defaulting to its root.</summary>
    public string? Directory { get; set; }

    /// <summary>An optional file filter applied before loading, defaulting to all Markdown files.</summary>
    public Func<FileInfo, bool>? FileFilter { get; set; }

    /// <summary>
    /// Optional CSS class applied to images rendered from markdown. Default: none.
    /// </summary>
    public string? ImageCssClass
    {
        get => Processing.ImageCssClass;
        set => Processing.ImageCssClass = value;
    }

    /// <summary>Registers a Markdig configuration applied after the default pipeline is configured.</summary>
    public void ConfigureMarkdig(Action<MarkdownPipelineBuilder> configure)
    {
        Processing.ConfigureMarkdig(configure);
    }

    /// <summary>Registers an HTML transformation applied after Markdown rendering, in registration order.</summary>
    public void AddHtmlTransform(Func<string, string> transform)
    {
        Processing.AddHtmlTransform(transform);
    }

    /// <summary>Registers a YAML deserializer configuration after the camelCase and ignore-unmatched-properties defaults.</summary>
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
