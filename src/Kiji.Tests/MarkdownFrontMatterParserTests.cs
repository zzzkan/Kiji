using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownFrontMatterParserTests
{
    [Fact]
    public void Parse_RequestedFrontMatterType_ParsesYaml()
    {
        var frontMatter = MarkdownFrontMatterParser.ParseContent<FrontMatter>(
            """
            ---
            title: Test Post
            description: A sample post
            createdAt: 2024-01-15
            tags:
              - test
              - sample
            ---

            Body.
            """);

        Assert.Equal("Test Post", frontMatter.Title);
        Assert.Equal("A sample post", frontMatter.Description);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 1, 15), frontMatter.CreatedAt.Value.DateTime);
        Assert.Equal(["test", "sample"], frontMatter.Tags);
    }

    [Fact]
    public void Parse_MissingFrontMatter_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MarkdownFrontMatterParser.ParseContent<FrontMatter>("# Missing\n\nNo front matter."));
    }

    [Fact]
    public void ParseContentAndBody_ExtractsBothFromOneDelimiterScan()
    {
        var parsed = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
            "---\r\ntitle: Combined\r\n---\r\n\r\nBody.\r\n",
            MarkdownFrontMatterParser.DefaultDeserializer);

        Assert.Equal("Combined", parsed.FrontMatter.Title);
        Assert.Equal("Body.\r\n", parsed.Body);
    }

}
