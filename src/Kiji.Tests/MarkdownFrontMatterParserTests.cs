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
    public void Parse_ConcreteFrontMatterType_ParsesYaml()
    {
        var markdownPath = CreateMarkdownFile(
            "front-matter.md",
            """
            ---
            title: Test Post
            createdAt: 2024-01-15
            tags:
              - test
              - sample
            ---

            Body.
            """);

        var frontMatter = MarkdownFrontMatterParser.Parse<FrontMatter>(markdownPath);

        Assert.Equal("Test Post", frontMatter.Title);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 1, 15), frontMatter.CreatedAt.Value.DateTime);
        Assert.Equal(["test", "sample"], frontMatter.Tags);
    }

    [Fact]
    public void Parse_GenericType_ParsesYamlIntoRequestedType()
    {
        var markdownPath = CreateMarkdownFile(
            "generic.md",
            """
            ---
            title: Generic Post
            createdAt: 2024-02-20
            description: Example
            ---

            Body.
            """);

        var frontMatter = MarkdownFrontMatterParser.Parse<TestFrontMatter>(markdownPath);

        Assert.Equal("Generic Post", frontMatter.Title);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 2, 20), frontMatter.CreatedAt.Value.DateTime);
        Assert.Equal("Example", frontMatter.Description);
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

        Assert.Throws<InvalidOperationException>(() => MarkdownFrontMatterParser.Parse<FrontMatter>(markdownPath));
    }

    private string CreateMarkdownFile(string fileName, string contents)
    {
        var path = Path.Combine(_testDir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class TestFrontMatter
    {
        public string? Title { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public string? Description { get; init; }
    }
}
