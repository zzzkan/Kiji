using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownFrontMatterParserTests
{
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
    [InlineData("# Body", "# Body")]
    [InlineData("---\n---\n# Body", "# Body")]
    [InlineData("---\r\n---\r\n# Body", "# Body")]
    [InlineData("---\n\n---\n# Body", "# Body")]
    [InlineData("---\n# draft\n---\n# Body", "# Body")]
    [InlineData("---\r\n# draft\r\n---\r\n# Body", "# Body")]
    [InlineData("---\n  # draft\n\n# aliases\n---", "")]
    [InlineData("---\n---", "")]
    public void Parse_OptionalFrontMatter_PreservesBodyAndModelDefaults(string markdown, string expectedBody)
    {
        var (frontMatter, body) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(markdown,
            new MarkdownOptions().CreateDeserializer());
        Assert.NotNull(frontMatter);
        Assert.Null(frontMatter.Title);
        Assert.Null(frontMatter.Tags);
        Assert.Equal(expectedBody, body);
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
    public void Parse_CanExplicitlyIgnoreUnknownKeys()
    {
        var options = new MarkdownOptions();
        options.Yaml.IgnoreUnmatchedProperties();
        var (frontMatter, body) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
            "---\ntitle: Imported\ndraft: false\naliases: [/old/]\n---\nBody", options.CreateDeserializer());
        Assert.Equal("Imported", frontMatter.Title);
        Assert.Equal("Body", body);
    }

    [Theory]
    [InlineData("2024-01-15", 0)]
    [InlineData("2024-01-15T12:30:00", 0)]
    [InlineData("2024-01-15T12:30:00Z", 0)]
    [InlineData("2024-01-15T12:30:00+09:00", 9)]
    public void Parse_DatesWithoutOffsetsAreUtcAndExplicitOffsetsSurvive(string value, int offsetHours)
    {
        var (frontMatter, _) = MarkdownFrontMatterParser.ParseContentAndBody<FrontMatter>(
            $"---\ncreatedAt: {value}\n---\nBody", new MarkdownOptions().CreateDeserializer());
        Assert.Equal(TimeSpan.FromHours(offsetHours), frontMatter.CreatedAt!.Value.Offset);
    }

    [Fact]
    public void Parse_DateTimeValuesAreUtcAndNullableDatesAcceptNull()
    {
        var (dates, _) = MarkdownFrontMatterParser.ParseContentAndBody<Dictionary<string, DateTime?>>(
            "---\ndate: 2024-01-15\nexplicit: 2024-01-15T12:30:00+09:00\nempty: null\n---\nBody",
            new MarkdownOptions().CreateDeserializer());
        Assert.Equal(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc), dates["date"]);
        Assert.Equal(DateTimeKind.Utc, dates["date"]!.Value.Kind);
        Assert.Equal(new DateTime(2024, 1, 15, 3, 30, 0, DateTimeKind.Utc), dates["explicit"]);
        Assert.Null(dates["empty"]);
    }
}
