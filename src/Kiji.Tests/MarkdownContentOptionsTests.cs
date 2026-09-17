using Kiji.Assets;
using Kiji.Markdown;
using Xunit;
using YamlDotNet.Serialization.NamingConventions;

namespace Kiji.Tests;

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

        var html = await ProcessFileAsync(processor, mdPath);

        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public async Task ConfigureMarkdig_CanRemoveSecureLinkExtension()
    {
        var mdPath = CreateMarkdownFile("insecure.md", "[External](https://example.com)");
        var processor = CreateProcessor(static options =>
            options.ConfigureMarkdig(static builder => builder.Extensions.TryRemove<SecureLinkExtension>()));

        var html = await ProcessFileAsync(processor, mdPath);

        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public async Task AddHtmlTransform_RunsInRegistrationOrder()
    {
        var mdPath = CreateMarkdownFile("order.md", "Body.");
        var processor = CreateProcessor(static options =>
        {
            options.AddHtmlTransform(static html => html + "<!--first-->");
            options.AddHtmlTransform(static html => html + "<!--second-->");
        });

        var html = await ProcessFileAsync(processor, mdPath);

        Assert.EndsWith("<!--first--><!--second-->", html.TrimEnd());
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
    public void ConfigureFrontMatter_CustomNamingConvention_IsApplied()
    {
        var options = new MarkdownContentOptions<MarkdownContent<FrontMatter>>();
        options.ConfigureFrontMatter(static builder => builder.WithNamingConvention(UnderscoredNamingConvention.Instance));
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

        var frontMatter = MarkdownFrontMatterParser.ParseContent<FrontMatter>(File.ReadAllText(mdPath), deserializer);

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

    private MarkdownProcessor CreateProcessor(Action<MarkdownProcessingOptions>? configure = null)
    {
        var contentOptions = new MarkdownProcessingOptions();
        configure?.Invoke(contentOptions);

        return new MarkdownProcessor(
            new ResolvedSitePaths
            {
                ContentDirectory = _testFilesDir,
                StaticDirectory = _testDir,
                OutputDirectory = _testDir,
            },
            new ImageProcessor(),
            contentOptions);
    }

    private static Task<string> ProcessFileAsync(MarkdownProcessor processor, string path)
    {
        var body = MarkdownFrontMatterParser.RemoveFrontMatter(File.ReadAllText(path));
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
