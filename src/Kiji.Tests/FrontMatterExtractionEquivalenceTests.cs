using System.Text.RegularExpressions;
using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Locks the span-based front matter extraction to the semantics of the original
/// <c>^---\s*\r?\n(.*?)\r?\n---\s*\r?\n</c> regex it replaced, using that regex as
/// the oracle over a corpus of realistic documents.
/// </summary>
public sealed partial class FrontMatterExtractionEquivalenceTests
{
    [GeneratedRegex(@"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline)]
    private static partial Regex OracleRegex();

    public static TheoryData<string> Corpus()
    {
        return
        [
            // Simple LF document with a blank line after the closing delimiter.
            "---\ntitle: Test\ncreatedAt: 2024-01-15\n---\n\n# Body\n\nText.\n",
            // CRLF document.
            "---\r\ntitle: Test\r\ncreatedAt: 2024-01-15\r\n---\r\n\r\n# Body\r\n",
            // No blank line between the closing delimiter and the body.
            "---\ntitle: Test\n---\n# Body\n",
            // Multiple blank lines after the closing delimiter (regex eats them greedily).
            "---\ntitle: Test\n---\n\n\n\n# Body\n",
            // Trailing spaces and tabs after both delimiters.
            "---  \t\ntitle: Test\n--- \t \n\nBody\n",
            // CRLF with trailing spaces after the delimiters.
            "---  \r\ntitle: Test\r\n---  \r\n\r\nBody\r\n",
            // A '---' inside the YAML block that is not a delimiter line.
            "---\ntitle: a---b\nseparator: '----'\n---\nBody\n",
            // A '----' line inside the YAML block (not a valid delimiter).
            "---\ntitle: Test\n----\nnote: below a ruler\n---\nBody\n",
            // Thematic break in the body after valid front matter.
            "---\ntitle: Test\n---\n\nIntro\n\n---\n\nOutro\n",
            // Empty front matter block (single blank line between delimiters).
            "---\n\n---\nBody\n",
            // Multi-line YAML with lists and nested indentation.
            "---\ntitle: Test\ntags:\n  - one\n  - two\nnested:\n  key: value\n---\n\nBody\n",
            // Missing front matter entirely.
            "# Just a heading\n\nBody text.\n",
            // Dashes that are not a front matter opener.
            "----\ntitle: nope\n----\n",
            // Opener with non-whitespace on the same line.
            "---title: nope\n---\n",
            // Unterminated front matter.
            "---\ntitle: Test\nnever closed\n",
            // Closing delimiter at EOF without a trailing newline (regex requires one).
            "---\ntitle: Test\n---",
            // Degenerate inputs.
            "",
            "---",
            "---\n",
            "---\n---\n",
        ];
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TryExtractFrontMatter_MatchesOracleRegex(string content)
    {
        var oracle = OracleRegex().Match(content);
        var extracted = MarkdownFrontMatterParser.TryExtractFrontMatter(content, out var yaml, out var body);

        Assert.Equal(oracle.Success, extracted);

        if (oracle.Success)
        {
            Assert.Equal(oracle.Groups[1].Value, content[yaml]);
            Assert.Equal(content[oracle.Length..], content[body]);
        }
    }

    [Fact]
    public void ParseContent_LeadingBlankLineInFrontMatter_StillDeserializes()
    {
        // The regex treated a blank line right after the opener as delimiter whitespace;
        // the span parser leaves it in the YAML block. YAML ignores leading blank lines,
        // so deserialization is unaffected either way.
        var frontMatter = MarkdownFrontMatterParser.ParseContent<FrontMatter>(
            "---\n\ntitle: Blank Lead\ncreatedAt: 2024-01-15\n---\nBody\n");

        Assert.Equal("Blank Lead", frontMatter.Title);
    }
}
