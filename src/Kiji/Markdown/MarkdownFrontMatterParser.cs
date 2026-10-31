using YamlDotNet.Serialization;

namespace Kiji.Markdown;

internal static class MarkdownFrontMatterParser
{
    internal static (TFrontMatter FrontMatter, string Body) ParseContentAndBody<TFrontMatter>(
        string content,
        IDeserializer deserializer)
        => ParseContentAndBody<TFrontMatter>(content, deserializer, out _);

    internal static (TFrontMatter FrontMatter, string Body) ParseContentAndBody<TFrontMatter>(
        string content,
        IDeserializer deserializer,
        out string frontMatterText)
    {
        var extracted = ExtractFrontMatter(content, out var yaml, out var body);
        frontMatterText = extracted ? content[yaml] : string.Empty;
        var frontMatter = deserializer.Deserialize<TFrontMatter>(extracted ? frontMatterText : "{}")
            ?? deserializer.Deserialize<TFrontMatter>("{}")
            ?? throw new InvalidOperationException("Failed to deserialize YAML front matter.");
        return (frontMatter, extracted ? content[body] : content);
    }

    /// <summary>
    /// Locates the YAML front matter block without allocating: an opening <c>---</c>
    /// line, the YAML payload, and a closing <c>---</c> line (each delimiter may carry
    /// trailing whitespace; the closer absorbs following blank lines).
    /// Returns the payload and body as ranges into
    /// <paramref name="content"/>.
    /// </summary>
    internal static bool ExtractFrontMatter(ReadOnlySpan<char> content, out Range yaml, out Range body)
    {
        yaml = default;
        body = default;

        var index = OpeningDelimiterEnd(content);
        if (index < 0)
        {
            var afterBlankLines = content;
            while (afterBlankLines.IndexOf('\n') is var newline && newline >= 0
                && afterBlankLines[..newline].Trim().IsEmpty)
            {
                afterBlankLines = afterBlankLines[(newline + 1)..];
            }
            if (OpeningDelimiterEnd(afterBlankLines) >= 0)
            {
                throw new InvalidOperationException("YAML front matter must start on the first line, without leading blank lines.");
            }
            return false;
        }

        if (index == content.Length)
        {
            throw new InvalidOperationException("YAML front matter is missing its closing '---' delimiter.");
        }

        var yamlStart = index + 1;

        // Search from the opening line's newline so the closer can immediately follow it.
        var searchFrom = index;
        while (true)
        {
            var offset = content[searchFrom..].IndexOf("\n---", StringComparison.Ordinal);
            if (offset < 0)
            {
                throw new InvalidOperationException("YAML front matter is missing its closing '---' delimiter.");
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

            if (lastNewline < 0 && runIndex != content.Length)
            {
                // Not a delimiter line (e.g. '----' or trailing text); keep scanning.
                searchFrom = closerStart;
                continue;
            }

            var yamlEnd = Math.Max(yamlStart, closerStart - 1);
            if (yamlEnd - 1 >= yamlStart && content[yamlEnd - 1] == '\r')
            {
                yamlEnd--;
            }

            yaml = yamlStart..yamlEnd;
            body = (lastNewline < 0 ? content.Length : lastNewline + 1)..content.Length;
            return true;
        }
    }

    private static int OpeningDelimiterEnd(ReadOnlySpan<char> content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal)) { return -1; }
        var index = 3;
        while (index < content.Length && content[index] is ' ' or '\t') { index++; }
        if (index < content.Length && content[index] == '\r') { index++; }
        return index == content.Length || content[index] == '\n' ? index : -1;
    }

    private static bool IsDelimiterWhitespace(char value)
    {
        return value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';
    }
}
