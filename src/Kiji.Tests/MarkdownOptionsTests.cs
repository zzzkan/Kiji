using Kiji.Assets;
using Kiji.Markdown;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.GenericAttributes;
using Xunit;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Tests;

public sealed class MarkdownOptionsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilesDir;

    public MarkdownOptionsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownOptionsTests_{Guid.NewGuid():N}");
        _testFilesDir = Path.Combine(_testDir, "files");
        Directory.CreateDirectory(_testFilesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task Defaults_UseGfmStyleOutputAndUnicodeHeadingIds()
    {
        var html = await CreateProcessor().ProcessBodyAsync("post.md", """
            # はじめに
            # はじめに
            ~x~ ~~y~~ ""z""

            [External](https://example.com){.external}
            ![Video](https://www.youtube.com/watch?v=abc)

            ```mermaid
            graph TD; A-->B
            ```

            | A | B |
            | - | - |
            | x | y |

            - [x] Task

            https://example.com

            Text[^1]

            [^1]: A footnote.
            """);
        Assert.Contains("id=\"はじめに\"", html);
        Assert.Contains("id=\"はじめに-1\"", html);
        Assert.Contains("<del>x</del>", html);
        Assert.Contains("<del>y</del>", html);
        Assert.DoesNotContain("<sub>", html);
        Assert.DoesNotContain("<cite>", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("<pre><code class=\"language-mermaid\">", html);
        Assert.Contains("<table>", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("class=\"external\"", html);
        Assert.Contains("class=\"footnotes\"", html);
        Assert.Contains("A footnote.", html);
        Assert.DoesNotContain("Text[^1]", html);
        Assert.DoesNotContain("target=\"_blank\"", html);
    }

    [Fact]
    public async Task Pipeline_CanReplaceDefaultsAndAddsAttributesLast()
    {
        var options = new MarkdownOptions();
        options.Pipeline.Extensions.Replace<AutoIdentifierExtension>(new AutoIdentifierExtension(AutoIdentifierOptions.Default));
        options.Pipeline.UseAdvancedExtensions();
        // Use* does not change an existing extension; replacement must take effect.
        options.Pipeline.Extensions.Replace<EmphasisExtraExtension>(new EmphasisExtraExtension(EmphasisExtraOptions.Default));
        options.Pipeline.UseEmojiAndSmiley();
        var processor = new MarkdownProcessor(new ResolvedSitePaths { OutputDirectory = _testDir }, new ImageProcessor(), options);
        var html = await processor.ProcessBodyAsync("post.md", """
            # はじめに

            [External](https://example.com){.custom}

            ~subscript~ ==marked== ^superscript^

            Term
            : Definition

            $x^2$

            ::: note
            Container
            :::
            """);
        Assert.Contains("id=\"section\"", html);
        Assert.Contains("class=\"custom\"", html);
        Assert.Contains("<sub>subscript</sub>", html);
        Assert.Contains("<mark>marked</mark>", html);
        Assert.Contains("<sup>superscript</sup>", html);
        Assert.Contains("<dl>", html);
        Assert.Contains("class=\"math\"", html);
        Assert.Contains("class=\"note\"", html);
        Assert.IsType<GenericAttributesExtension>(options.Pipeline.Extensions.Last());
    }

    [Theory]
    [InlineData("A / B", "a--b")]
    [InlineData("Foo - Bar", "foo---bar")]
    [InlineData("C# & .NET 10", "c--net-10")]
    public async Task Defaults_UseGitHubPunctuationRulesAndAllowExplicitHeadingIds(string heading, string expectedId)
    {
        var html = await CreateProcessor().ProcessBodyAsync("post.md", $"# {heading}\n\n## {heading} {{#legacy}}");
        Assert.Contains($"id=\"{expectedId}\"", html);
        Assert.Contains("id=\"legacy\"", html);
    }

    [Fact]
    public async Task ExternalAndSiteRootImages_RenderWithoutPageRenderContext()
    {
        var mdPath = CreateMarkdownFile(
            "plain-image.md",
            "![External](https://example.com/a.png)\n\n![Static](/icons/b.png)");
        var processor = CreateProcessor();

        var html = await ProcessFileAsync(processor, mdPath);

        Assert.Contains("src=\"https://example.com/a.png\"", html);
        Assert.Contains("src=\"/icons/b.png\"", html);
        Assert.DoesNotContain("srcset", html);
    }

    [Fact]
    public void ConfigureYaml_CustomNamingConvention_IsApplied()
    {
        var options = new MarkdownOptions();
        options.Yaml.WithNamingConvention(UnderscoredNamingConvention.Instance);
        var deserializer = options.CreateDeserializer();
        var mdPath = CreateMarkdownFile(
            "underscored.md",
            """
            ---
            title: Snake Case
            created_at: 2024-03-01
            ---

            Body.
            """);

        var frontMatter = MarkdownSourceReader.Read<FrontMatter>(new FileInfo(mdPath), deserializer).FrontMatter;

        Assert.Equal("Snake Case", frontMatter.Title);
        Assert.True(frontMatter.CreatedAt.HasValue);
        Assert.Equal(new DateTime(2024, 3, 1), frontMatter.CreatedAt.Value.DateTime);
    }

    [Fact]
    public async Task ProcessAsync_ParallelRendersOverSharedPipeline_AreIsolated()
    {
        var processor = CreateProcessor();
        var paths = Enumerable.Range(0, 20)
            .Select(i => CreateMarkdownFile($"parallel-{i}.md", $"# Title {i}\n\nBody {i}."))
            .ToArray();

        var results = await Task.WhenAll(paths.Select(path => ProcessFileAsync(processor, path)));

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Contains($"Title {i}", results[i]);
            Assert.Contains($"Body {i}.", results[i]);
        }
    }

    private MarkdownProcessor CreateProcessor(Action<MarkdownOptions>? configure = null)
    {
        var contentOptions = new MarkdownOptions();
        configure?.Invoke(contentOptions);

        return new MarkdownProcessor(
            new ResolvedSitePaths
            {
                OutputDirectory = _testDir,
            },
            new ImageProcessor(),
            contentOptions);
    }

    private static Task<string> ProcessFileAsync(MarkdownProcessor processor, string path)
    {
        var body = MarkdownSourceReader.Read<FrontMatter>(new FileInfo(path), new MarkdownOptions().CreateDeserializer()).Body;
        return processor.ProcessBodyAsync(path, body);
    }

    private string CreateMarkdownFile(string fileName, string body)
    {
        var content = body.StartsWith("---", StringComparison.Ordinal)
            ? body
            : $"""
              ---
              title: Test
              createdAt: 2024-01-15
              ---

              {body}
              """;

        var path = Path.Combine(_testFilesDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
