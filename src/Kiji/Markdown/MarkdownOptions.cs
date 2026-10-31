using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

/// <summary>Site-wide Markdown and YAML settings, configured through DI options.</summary>
public sealed class MarkdownOptions
{
    private readonly Lock _yamlLock = new();

    /// <summary>The Markdig builder. Defaults to GFM-style tables, task lists, autolinks and strikethrough, footnotes, GitHub heading IDs, and generic attributes.</summary>
    /// <remarks>Replace or remove existing extensions through <c>Pipeline.Extensions</c>.</remarks>
    public MarkdownPipelineBuilder Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseFootnotes()
        .UseGenericAttributes();

    /// <summary>The YAML builder. Defaults to camelCase, case-insensitive property matching, strict unknown keys, and UTC for dates without offsets.</summary>
    public DeserializerBuilder Yaml { get; } = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithCaseInsensitivePropertyMatching()
        .WithTypeConverter(new MarkdownDateTimeConverter());

    internal IDeserializer CreateDeserializer()
    {
        // Build serially; each parsing worker owns its own deserializer.
        lock (_yamlLock) { return Yaml.Build(); }
    }
}
