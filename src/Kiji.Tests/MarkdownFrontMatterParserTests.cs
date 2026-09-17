using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownFrontMatterParserTests : IDisposable
{
    private readonly string _testDir;

    public MarkdownFrontMatterParserTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownFrontMatterParserTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void Parse_RequestedFrontMatterType_ParsesYaml()
    {
        var markdownPath = CreateMarkdownFile(
            "front-matter.md",
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

        var frontMatter = MarkdownFrontMatterParser.ParseContent<FrontMatter>(File.ReadAllText(markdownPath));

        Assert.Equal("Test Post", frontMatter.Title);
        Assert.Equal("A sample post", frontMatter.Description);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 1, 15), frontMatter.CreatedAt.Value.DateTime);
        Assert.Equal(["test", "sample"], frontMatter.Tags);
    }

    [Fact]
    public void Parse_MissingFrontMatter_ThrowsInvalidOperationException()
    {
        var markdownPath = CreateMarkdownFile(
            "missing.md",
            """
            # Missing

            No front matter.
            """);

        Assert.Throws<InvalidOperationException>(() =>
            MarkdownFrontMatterParser.ParseContent<FrontMatter>(File.ReadAllText(markdownPath)));
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

    private string CreateMarkdownFile(string fileName, string contents)
    {
        var path = Path.Combine(_testDir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

}
