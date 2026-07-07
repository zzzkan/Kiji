using System.Text.RegularExpressions;
using Kiji.Assets;
using Kiji.Markdown;
using Xunit;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="MarkdownContentOptions"/> driving <see cref="MarkdownProcessor"/>
/// and front matter deserialization.
/// </summary>
public sealed class MarkdownContentOptionsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilesDir;

    public MarkdownContentOptionsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownContentOptionsTests_{Guid.NewGuid():N}");
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
    public async Task Default_ExternalLink_GetsSecureAttributes()
    {
        var mdPath = CreateMarkdownFile("secure.md", "[External](https://example.com)");
        var processor = CreateProcessor();

        var html = await processor.ProcessAsync(mdPath);

        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public async Task ConfigurePipeline_CanRemoveSecureLinkExtension()
    {
        var mdPath = CreateMarkdownFile("insecure.md", "[External](https://example.com)");
        var processor = CreateProcessor(static options =>
            options.ConfigurePipeline(static builder => builder.Extensions.TryRemove<SecureLinkExtension>()));

        var html = await processor.ProcessAsync(mdPath);

        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public async Task AddHtmlPostProcessor_CanAddHeadingAnchors()
    {
        var mdPath = CreateMarkdownFile("anchors.md", "## Section Title");
        var processor = CreateProcessor(static options =>
            options.AddHtmlPostProcessor(static html => Regex.Replace(
                html,
                "<h([1-6]) id=\"([^\"]+)\">",
                "<h$1 id=\"$2\"><a class=\"anchor\" href=\"#$2\"></a>")));

        var html = await processor.ProcessAsync(mdPath);

        Assert.Contains("<h2 id=\"section-title\"><a class=\"anchor\" href=\"#section-title\"></a>Section Title</h2>", html);
    }

    [Fact]
    public async Task AddHtmlPostProcessor_RunsInRegistrationOrder()
    {
        var mdPath = CreateMarkdownFile("order.md", "Body.");
        var processor = CreateProcessor(static options => options
            .AddHtmlPostProcessor(static html => html + "<!--first-->")
            .AddHtmlPostProcessor(static html => html + "<!--second-->"));

        var html = await processor.ProcessAsync(mdPath);

        Assert.EndsWith("<!--first--><!--second-->", html.TrimEnd());
    }

    [Fact]
    public async Task NullImageBackend_RendersPlainImg_AndSkipsAssetOutput()
    {
        var mdPath = CreateMarkdownFile("plain-image.md", "![Alt](local.png)");
        var processor = CreateProcessor();

        var html = await processor.ProcessAsync(mdPath);

        Assert.Contains("<img src=\"local.png\" alt=\"Alt\"", html);
        Assert.DoesNotContain("srcset", html);
        Assert.DoesNotContain("class=\"blog-image\"", html);
        Assert.False(Directory.Exists(Path.Combine(_testDir, "test-assets")));
    }

    [Fact]
    public void ConfigureFrontMatter_CustomNamingConvention_IsApplied()
    {
        var options = new MarkdownContentOptions()
            .ConfigureFrontMatter(static builder => builder.WithNamingConvention(UnderscoredNamingConvention.Instance));
        var deserializer = MarkdownFrontMatterParser.CreateDeserializer(options.FrontMatterConfigurations);
        var mdPath = CreateMarkdownFile(
            "underscored.md",
            """
            ---
            title: Snake Case
            created_at: 2024-03-01
            ---

            Body.
            """);

        var frontMatter = MarkdownFrontMatterParser.Parse<FrontMatter>(mdPath, deserializer);

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

        var results = await Task.WhenAll(paths.Select(path => processor.ProcessAsync(path)));

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Contains($"Title {i}", results[i]);
            Assert.Contains($"Body {i}.", results[i]);
        }
    }

    private MarkdownProcessor CreateProcessor(Action<MarkdownContentOptions>? configure = null)
    {
        var contentOptions = new MarkdownContentOptions();
        configure?.Invoke(contentOptions);

        return new MarkdownProcessor(
            new SsgOptions
            {
                ContentsPath = _testFilesDir,
                StaticPath = _testDir,
                OutputPath = _testDir,
                AssetsDirectoryName = "test-assets",
            },
            new NullImageAssetProcessor(),
            contentOptions);
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
