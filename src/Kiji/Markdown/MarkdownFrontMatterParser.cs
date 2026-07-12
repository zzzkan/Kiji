using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

public static partial class MarkdownFrontMatterParser
{
    internal static readonly IDeserializer DefaultDeserializer = CreateDeserializer(configurations: null);

    public static TFrontMatter Parse<TFrontMatter>(string filePath)
    {
        return Parse<TFrontMatter>(filePath, DefaultDeserializer);
    }

    /// <summary>
    /// Parses the YAML front matter of a markdown file with a custom deserializer.
    /// </summary>
    public static TFrontMatter Parse<TFrontMatter>(string filePath, IDeserializer deserializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(deserializer);

        var content = File.ReadAllText(filePath);
        try
        {
            return ParseContent<TFrontMatter>(content, deserializer);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to parse YAML front matter of '{filePath}'. {exception.Message}",
                exception);
        }
    }

    internal static TFrontMatter ParseContent<TFrontMatter>(string content)
    {
        return ParseContent<TFrontMatter>(content, DefaultDeserializer);
    }

    internal static TFrontMatter ParseContent<TFrontMatter>(string content, IDeserializer deserializer)
    {
        var yaml = ExtractFrontMatterYaml(content);
        var frontMatter = deserializer.Deserialize<TFrontMatter>(yaml);

        return frontMatter is not null
            ? frontMatter
            : throw new InvalidOperationException("Failed to deserialize YAML front matter.");
    }

    internal static IDeserializer CreateDeserializer(IReadOnlyList<Action<DeserializerBuilder>>? configurations)
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties();

        if (configurations is not null)
        {
            foreach (var configure in configurations)
            {
                configure(builder);
            }
        }

        return builder.Build();
    }

    internal static string RemoveFrontMatter(string content)
    {
        var match = MatchFrontMatter(content);
        return content[match.Length..];
    }

    private static string ExtractFrontMatterYaml(string content)
    {
        var match = MatchFrontMatter(content);
        return match.Groups[1].Value;
    }

    private static Match MatchFrontMatter(string content)
    {
        var match = FrontMatterRegex().Match(content);
        if (!match.Success)
        {
            throw new InvalidOperationException("YAML front matter not found.");
        }

        return match;
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
