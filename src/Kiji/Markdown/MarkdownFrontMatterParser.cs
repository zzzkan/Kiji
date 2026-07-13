using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

public static class MarkdownFrontMatterParser
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
        if (!TryExtractFrontMatter(content, out var yaml, out _))
        {
            throw new InvalidOperationException("YAML front matter not found.");
        }

        var frontMatter = deserializer.Deserialize<TFrontMatter>(content[yaml]);

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
        if (!TryExtractFrontMatter(content, out _, out var body))
        {
            throw new InvalidOperationException("YAML front matter not found.");
        }

        return content[body];
    }

    /// <summary>
    /// Locates the YAML front matter block without allocating: an opening <c>---</c>
    /// line, the YAML payload, and a closing <c>---</c> line (each delimiter may carry
    /// trailing whitespace; the closer greedily absorbs following blank lines, matching
    /// the regex this replaced). Returns the payload and body as ranges into
    /// <paramref name="content"/>.
    /// </summary>
    internal static bool TryExtractFrontMatter(ReadOnlySpan<char> content, out Range yaml, out Range body)
    {
        yaml = default;
        body = default;

        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return false;
        }

        // Opening delimiter line: '---' plus optional spaces/tabs, ended by a line break.
        var index = 3;
        while (index < content.Length && content[index] is ' ' or '\t')
        {
            index++;
        }

        if (index < content.Length && content[index] == '\r')
        {
            index++;
        }

        if (index >= content.Length || content[index] != '\n')
        {
            return false;
        }

        var yamlStart = index + 1;

        // Closing delimiter: the first line starting with '---' whose remainder is
        // whitespace containing a newline. The block ends at the last newline of that
        // whitespace run (the regex's greedy `\s*\r?\n` also swallowed blank lines).
        var searchFrom = yamlStart;
        while (true)
        {
            var offset = content[searchFrom..].IndexOf("\n---", StringComparison.Ordinal);
            if (offset < 0)
            {
                return false;
            }

            var closerStart = searchFrom + offset + 1;
            var runIndex = closerStart + 3;
            var lastNewline = -1;
            while (runIndex < content.Length && IsDelimiterWhitespace(content[runIndex]))
            {
                if (content[runIndex] == '\n')
                {
                    lastNewline = runIndex;
                }

                runIndex++;
            }

            if (lastNewline < 0)
            {
                // Not a delimiter line (e.g. '----' or trailing text); keep scanning.
                searchFrom = closerStart;
                continue;
            }

            var yamlEnd = closerStart - 1;
            if (yamlEnd - 1 >= yamlStart && content[yamlEnd - 1] == '\r')
            {
                yamlEnd--;
            }

            yaml = yamlStart..yamlEnd;
            body = (lastNewline + 1)..content.Length;
            return true;
        }
    }

    private static bool IsDelimiterWhitespace(char value)
    {
        return value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';
    }
}
