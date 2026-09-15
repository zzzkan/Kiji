using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class FrontMatterExtractionEquivalenceTests
{
    [Theory]
    [InlineData("---\ntitle: Test\n---\n# Body\n", "title: Test", "# Body\n")]
    [InlineData("---  \r\ntitle: Test\r\n---  \r\n\r\nBody\r\n", "title: Test", "Body\r\n")]
    [InlineData("---\ntitle: a---b\n----\nnote: text\n---\nIntro\n\n---\nOutro", "title: a---b\n----\nnote: text", "Intro\n\n---\nOutro")]
    [InlineData("---\n\n---\nBody", "", "Body")]
    [InlineData("# Body", null, null)]
    [InlineData("----\ntitle: Test\n---\nBody", null, null)]
    [InlineData("---title: Test\n---\nBody", null, null)]
    [InlineData("---\ntitle: Test", null, null)]
    [InlineData("---\ntitle: Test\n---", null, null)]
    public void ExtractsDelimitedYamlAndPreservesBody(string content, string? expectedYaml, string? expectedBody)
    {
        var extracted = MarkdownFrontMatterParser.TryExtractFrontMatter(content, out var yaml, out var body);
        Assert.Equal(expectedYaml is not null, extracted);
        if (extracted)
        {
            Assert.Equal(expectedYaml, content[yaml]);
            Assert.Equal(expectedBody, content[body]);
        }
    }

    [Fact]
    public void LeadingBlankLineInFrontMatter_Deserializes()
    {
        var frontMatter = MarkdownFrontMatterParser.ParseContent<FrontMatter>(
            "---\n\ntitle: Blank Lead\ncreatedAt: 2024-01-15\n---\nBody\n");
        Assert.Equal("Blank Lead", frontMatter.Title);
    }
}
