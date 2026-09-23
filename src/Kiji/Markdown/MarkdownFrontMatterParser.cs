using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Markdown;

internal static class MarkdownFrontMatterParser
{
    internal static (TFrontMatter FrontMatter, string Body) ParseContentAndBody<TFrontMatter>(
        string content,
        IDeserializer deserializer)
    {
        if (!TryExtractFrontMatter(content, out var yaml, out var body))
        {
            throw new InvalidOperationException("YAML front matter not found.");
        }

        var frontMatter = deserializer.Deserialize<TFrontMatter>(content[yaml])
            ?? throw new InvalidOperationException("Failed to deserialize YAML front matter.");
        return (frontMatter, content[body]);
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

    /// <summary>
    /// Locates the YAML front matter block without allocating: an opening <c>---</c>
    /// line, the YAML payload, and a closing <c>---</c> line (each delimiter may carry
    /// trailing whitespace; the closer absorbs following blank lines).
    /// Returns the payload and body as ranges into
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
        // whitespace run, including blank lines.
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
