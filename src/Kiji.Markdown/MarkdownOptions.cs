using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

/// <summary>Markdown and YAML settings for the whole site; configure them as DI options.</summary>
public sealed class MarkdownOptions
{
    private readonly Lock _yamlLock = new();

    /// <summary>The Markdig pipeline: GitHub-style tables, task lists, autolinks, strikethrough, footnotes, heading IDs and attributes by default.</summary>
    public MarkdownPipelineBuilder Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseFootnotes()
        .UseGenericAttributes();

    /// <summary>The front matter deserializer: camelCase, case-insensitive, unknown keys rejected, dates without offset in UTC.</summary>
    public DeserializerBuilder Yaml { get; } = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithCaseInsensitivePropertyMatching()
        .WithTypeConverter(new MarkdownDateTimeConverter())
        .WithTypeInspector(static inner => new CachedPropertyTypeInspector(inner), static where => where.OnTop());

    internal IDeserializer CreateDeserializer()
    {
        lock (_yamlLock) { return Yaml.Build(); }
    }
}
