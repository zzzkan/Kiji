using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownFrontMatterParserTests
{
    [Theory]
    [InlineData("---")]
    [InlineData("---\ntitle: Hello")]
    [InlineData("---\r\ntitle: Hello\r\n----\r\nBody")]
    public void Parse_UnclosedFrontMatter_Fails(string markdown)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(markdown, new MarkdownOptions().CreateDeserializer()));
        Assert.Contains("closing '---'", error.Message);
    }

    [Theory]
    [InlineData("\n---\ntitle: Hello\n---\nBody")]
    [InlineData(" \t\r\n\r\n---\r\ntitle: Hello")]
    public void Parse_FrontMatterAfterBlankLines_Fails(string markdown)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(markdown, new MarkdownOptions().CreateDeserializer()));
        Assert.Contains("first line", error.Message);
    }

    [Theory]
    [InlineData("    ---\n    Code block")]
    [InlineData("\n    ---\n    Code block")]
    [InlineData("\n# Heading\n\n---\nBody")]
    public void Parse_OrdinaryMarkdown_PreservesBody(string markdown)
    {
        var (frontMatter, body) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
            markdown, new MarkdownOptions().CreateDeserializer());
        Assert.Null(frontMatter.Title);
        Assert.Equal(markdown, body);
    }

    [Theory]
    [InlineData("10:30")]
    [InlineData("January 15")]
    public void Parse_IncompleteDates_FailForBothDateTypes(string value)
    {
        var markdown = $"---\ndate: \"{value}\"\n---\n";
        var deserializer = new MarkdownOptions().CreateDeserializer();
        Assert.Throws<YamlDotNet.Core.YamlException>(() =>
            MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, DateTimeOffset>>(markdown, deserializer));
        Assert.Throws<YamlDotNet.Core.YamlException>(() =>
            MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, DateTime?>>(markdown, deserializer));
    }

    [Fact]
    public void Parse_RequestedFrontMatterType_ParsesYaml()
    {
        var (frontMatter, body) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
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
            """, new MarkdownOptions().CreateDeserializer());

        Assert.Equal("Test Post", frontMatter.Title);
        Assert.Equal("A sample post", frontMatter.Description);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 1, 15), frontMatter.CreatedAt.Value.DateTime);
        Assert.Equal(["test", "sample"], frontMatter.Tags);
        Assert.Equal("Body.", body);
    }

    [Theory]
    [InlineData("# Body")]
    [InlineData("---\n# draft\n---\n# Body")]
    public void Parse_OptionalFrontMatter_UsesModelDefaults(string markdown)
    {
        var (frontMatter, body) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(markdown,
            new MarkdownOptions().CreateDeserializer());
        Assert.Null(frontMatter.Title);
        Assert.Equal("# Body", body);
    }

    [Fact]
    public void Parse_KeyCasingIsAcceptedAndUnknownKeysFail()
    {
        var deserializer = new MarkdownOptions().CreateDeserializer();
        var (frontMatter, _) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
            "---\nTitle: Post\nTags: [a, b]\n---\nBody", deserializer);
        Assert.Equal("Post", frontMatter.Title);
        Assert.Equal(["a", "b"], frontMatter.Tags);
        var error = Assert.Throws<YamlDotNet.Core.YamlException>(() =>
            MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>("---\ncreated_at: 2024-01-15\n---\nBody", deserializer));
        Assert.Contains("created_at", error.Message);
    }

    [Fact]
    public void Parse_DatesWithoutOffsetsAreUtc()
    {
        var deserializer = new MarkdownOptions().CreateDeserializer();
        var (offsets, _) = MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, DateTimeOffset?>>(
            "---\nlocal: 2024-01-15T12:30:00\nexplicit: 2024-01-15T12:30:00+09:00\nempty: null\n---\n", deserializer);
        Assert.Equal(TimeSpan.Zero, offsets["local"]!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(9), offsets["explicit"]!.Value.Offset);
        Assert.Null(offsets["empty"]);
        var (dates, _) = MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, DateTime>>(
            "---\ndate: 2024-01-15\n---\n", deserializer);
        Assert.Equal(DateTimeKind.Utc, dates["date"].Kind);
        Assert.Equal(new DateTime(2024, 1, 15), dates["date"]);
    }
}
