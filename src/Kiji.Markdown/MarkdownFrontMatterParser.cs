using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

public static partial class MarkdownFrontMatterParser
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static TFrontMatter Parse<TFrontMatter>(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var content = File.ReadAllText(filePath);
        return ParseContent<TFrontMatter>(content);
    }

    internal static TFrontMatter ParseContent<TFrontMatter>(string content)
    {
        var yaml = ExtractFrontMatterYaml(content);
        var frontMatter = YamlDeserializer.Deserialize<TFrontMatter>(yaml);

        return frontMatter is not null
            ? frontMatter
            : throw new InvalidOperationException("Failed to deserialize YAML front matter.");
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
