using Kiji.Images;
using Kiji.Markdown;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Integration tests for <see cref="MarkdownContentsBuilder{TFrontMatter}"/>.
/// </summary>
public sealed class MarkdownContentsBuilderTests : IDisposable
{
    private const string AssetsDirectoryName = "_assets";
    private readonly MarkdownProcessor _markdownProcessor;
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _outputDir;

    public MarkdownContentsBuilderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownContentsBuilderTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "contents");
        _outputDir = Path.Combine(_testDir, "output");
        Directory.CreateDirectory(_contentsDir);
        Directory.CreateDirectory(_outputDir);
        _markdownProcessor = new MarkdownProcessor(new SsgOptions
        {
            ContentsPath = _contentsDir,
            StaticPath = _testDir,
            OutputPath = _outputDir,
        }, new ImageProcessor());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_SinglePost_ReturnsFrontMatterAndExplicitRenderOutput()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "test-post.md"),
            CreateValidMarkdown("Test Post", new DateTime(2024, 1, 15)));

        var contents = CreateBuilder().Build();

        var item = Assert.Single(contents);
        Assert.Equal("test-post", item.FileInfo.FileNameWithoutExtension);
        Assert.Equal("Test Post", item.FrontMatter.Title);
        Assert.Contains("Test content.", await item.RenderAsync(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_outputDir, AssetsDirectoryName)));
    }

    [Fact]
    public async Task ProcessAsync_MultiplePosts_ReturnsAllMarkdownItems()
    {
        var inputs = new[]
        {
            ("first-post.md", "First Post", new DateTime(2024, 1, 10)),
            ("second-post.md", "Second Post", new DateTime(2024, 1, 15)),
            ("third-post.md", "Third Post", new DateTime(2024, 1, 5)),
        };

        foreach (var (fileName, title, createdAt) in inputs)
        {
            await File.WriteAllTextAsync(Path.Combine(_contentsDir, fileName), CreateValidMarkdown(title, createdAt));
        }

        var contents = CreateBuilder().Build();

        Assert.Equal(["first-post", "second-post", "third-post"], contents.Select(static item => item.FileInfo.FileNameWithoutExtension));
    }

    [Fact]
    public async Task ProcessAsync_IndexFile_PreservesSourceInfoForSiteProjection()
    {
        var postDirectory = Path.Combine(_contentsDir, "my-awesome-post");
        Directory.CreateDirectory(postDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(postDirectory, "index.md"),
            CreateValidMarkdown("Awesome Post", new DateTime(2024, 1, 15)));

        var contents = CreateBuilder().Build();
        var item = Assert.Single(contents);

        Assert.Equal("index", item.FileInfo.FileNameWithoutExtension);
        Assert.Equal("my-awesome-post", item.FileInfo.RelativeDirectoryPath);
    }

    [Fact]
    public async Task ProcessAsync_NewBuildPicksUpUpdatedMarkdownWhileExistingContentsRemainStable()
    {
        var markdownPath = Path.Combine(_contentsDir, "update-test.md");
        await File.WriteAllTextAsync(markdownPath, CreateValidMarkdown("Update Test", new DateTime(2024, 1, 15), "Original content."));

        var firstContents = CreateBuilder().Build();
        var firstItem = Assert.Single(firstContents, static item => item.FileInfo.FileNameWithoutExtension == "update-test");
        Assert.Contains("Original content.", await firstItem.RenderAsync(), StringComparison.Ordinal);

        await File.WriteAllTextAsync(markdownPath, CreateValidMarkdown("Update Test", new DateTime(2024, 1, 15), "Updated content."));

        var secondContents = CreateBuilder().Build();
        var secondItem = Assert.Single(secondContents, static item => item.FileInfo.FileNameWithoutExtension == "update-test");

        Assert.Contains("Original content.", await firstItem.RenderAsync(), StringComparison.Ordinal);
        Assert.Contains("Updated content.", await secondItem.RenderAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentCollection_CanonicalSlugCollision_ThrowsBeforeWritingOutputs()
    {
        var firstDirectory = Path.Combine(_contentsDir, "2024");
        var secondDirectory = Path.Combine(_contentsDir, "2025");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);

        var firstPath = Path.Combine(firstDirectory, "Hello World!.md");
        var secondPath = Path.Combine(secondDirectory, "hello-world.md");
        await File.WriteAllTextAsync(firstPath, CreateValidMarkdown("First Post", new DateTime(2024, 1, 15)));
        await File.WriteAllTextAsync(secondPath, CreateValidMarkdown("Second Post", new DateTime(2024, 2, 15)));

        var contents = CreateBuilder().Build();
        var posts = Content.FromItems([.. contents.Select(Post.Create)]).WithKey(static post => post.Slug);
        var exception = Assert.Throws<InvalidOperationException>(() => posts.Items);

        Assert.Contains("hello-world", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate key", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_outputDir, AssetsDirectoryName)));
    }

    [Fact]
    public async Task ProcessAsync_SourceNotFound_ThrowsException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new SsgOptions
        {
            ContentsPath = Path.Combine(_testDir, "non-existent"),
            StaticPath = _testDir,
            OutputPath = _outputDir,
        });
    }

    [Fact]
    public async Task Post_Create_MissingRequiredFrontMatter_ThrowsException()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "no-title.md"),
            """
            ---
            createdAt: 2024-01-15
            ---

            Content without title.
            """);

        var contents = CreateBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => contents.Select(Post.Create).ToList());
    }

    [Fact]
    public async Task ProcessAsync_MetadataOnlyBuildDoesNotRenderUntilRequested()
    {
        var markdownPath = Path.Combine(_contentsDir, "convert-test.md");
        await File.WriteAllTextAsync(markdownPath, CreateValidMarkdown("Convert Test", new DateTime(2024, 4, 1), "This is some **bold** text."));

        var contents = CreateBuilder().Build();
        var item = Assert.Single(contents, static item => item.FileInfo.FileNameWithoutExtension == "convert-test");

        Assert.Equal("Convert Test", item.FrontMatter.Title);
        Assert.Equal("convert-test", item.FileInfo.FileNameWithoutExtension);
        Assert.Contains("<strong>bold</strong>", await item.RenderAsync(), StringComparison.Ordinal);
    }

    private MarkdownContentsBuilder<FrontMatter> CreateBuilder()
    {
        return new MarkdownContentsBuilder<FrontMatter>(_contentsDir, RenderAsync);
    }

    private Task<string> RenderAsync(MarkdownContent<FrontMatter> content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        return _markdownProcessor.ProcessAsync(content.FileInfo.FilePath, cancellationToken);
    }

    private static string CreateValidMarkdown(string title, DateTime createdAt, string content = "Test content.", List<string>? tags = null)
    {
        var tagsSection = tags is not null && tags.Count > 0
            ? $"tags:\n{string.Join("\n", tags.Select(tag => $"  - {tag}"))}\n"
            : string.Empty;

        return $$"""
            ---
            title: {{title}}
            createdAt: {{createdAt:yyyy-MM-dd}}
            {{tagsSection}}---

            {{content}}
            """;
    }
}
