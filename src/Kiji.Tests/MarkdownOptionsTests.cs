using Kiji.Assets;
using Kiji.Markdown;
using Markdig;
using Xunit;

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
    public async Task Defaults_RenderUnicodeHeadingIdsAndStrikethrough()
    {
        var html = await CreateProcessor().ProcessBodyAsync("post.md", "# はじめに\n# はじめに\n\n~~gone~~");
        Assert.Contains("id=\"はじめに\"", html);
        Assert.Contains("id=\"はじめに-1\"", html);
        Assert.Contains("<del>gone</del>", html);
    }

    [Fact]
    public async Task Pipeline_ExtensionsAddedAfterDefaultsStillParseAttributes()
    {
        var options = new MarkdownOptions();
        options.Pipeline.UseCustomContainers();
        var processor = new MarkdownProcessor(new ResolvedSitePaths { OutputDirectory = _testDir }, new ImageProcessor(), options);

        var html = await processor.ProcessBodyAsync("post.md", "::: note {#tip}\nBody\n:::");

        Assert.Contains("id=\"tip\"", html);
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
