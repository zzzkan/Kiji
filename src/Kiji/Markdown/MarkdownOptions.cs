using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

/// <summary>Site-wide Markdown and YAML settings, configured through DI options.</summary>
/// <remarks>Configure before the site runs; configuration changes require a restart during development. The builders expose Markdig and YamlDotNet APIs and follow those packages' compatibility rules.</remarks>
public sealed class MarkdownOptions
{
    private readonly Lock _yamlLock = new();

    /// <summary>The Markdig builder. Defaults to GFM-style tables, task lists, autolinks and strikethrough, footnotes, GitHub heading IDs, and generic attributes.</summary>
    /// <remarks>Replace or remove existing extensions through <c>Pipeline.Extensions</c>. Kiji moves GenericAttributes to the end before building.</remarks>
    public MarkdownPipelineBuilder Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .Use(new GfmStrikethroughExtension())
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
