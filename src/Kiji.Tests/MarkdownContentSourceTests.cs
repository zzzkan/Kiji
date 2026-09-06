using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for the markdown content source's file selection
/// (<see cref="MarkdownContentOptions{TModel}.Directory"/> and
/// <see cref="MarkdownContentOptions{TModel}.Where"/>), its projection, and the
/// page-bundle slug convention.
/// </summary>
public sealed class MarkdownContentSourceTests : IDisposable
{
    private readonly string _testDir;

    public MarkdownContentSourceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownContentSourceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testDir, "contents"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("hello.md", "hello")]
    [InlineData("hello/index.md", "hello")]
    [InlineData("hello/INDEX.md", "hello")]
    [InlineData("nested/hello/index.md", "hello")]
    [InlineData("nested/hello.md", "hello")]
    public void Slug_FollowsThePageBundleConvention(string relativePath, string expected)
    {
        var contentsDir = Path.Combine(_testDir, "contents");
        var filePath = Path.Combine(contentsDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "body");

        var fileInfo = MarkdownFileInfo.Create(contentsDir, filePath);

        Assert.Equal(expected, fileInfo.Slug);
    }

    [Fact]
    public void Directory_LimitsTheSourceToThatSubtree()
    {
        WriteMarkdown("posts/first.md", "First");
        WriteMarkdown("notes/alpha.md", "Alpha");

        var posts = LoadKeys(static options => options.Directory = "posts");

        Assert.Equal(["first"], posts);
    }

    [Fact]
    public void Where_FiltersDiscoveredFiles()
    {
        WriteMarkdown("published.md", "Published");
        WriteMarkdown("_draft.md", "Draft");

        var keys = LoadKeys(static options =>
            options.Where = file => !file.FileNameWithoutExtension.StartsWith('_'));

        Assert.Equal(["published"], keys);
    }

    [Fact]
    public void Directory_EscapingTheContentDirectory_Throws()
    {
        WriteMarkdown("inside.md", "Inside");

        var exception = Assert.Throws<InvalidOperationException>(
            () => LoadKeys(static options => options.Directory = "../outside"));

        Assert.Contains("resolves outside the content directory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_ProducesTheModel()
    {
        WriteMarkdown("second.md", "Second", order: 2);
        WriteMarkdown("first.md", "First", order: 1);

        var builder = CreateBuilder();
        builder.AddMarkdownContent<OrderedFrontMatter, ScopedNote>(
            select: static content => ScopedNote.Create(new MarkdownContent<FrontMatter>(
                content.FileInfo,
                new FrontMatter { Title = content.FrontMatter.Title },
                static (_, _) => Task.FromResult(string.Empty))),
            key: static note => note.Key);

        var app = builder.Build();
        app.UsePlanningOptions();
        var notes = app.Services.GetRequiredService<ContentDictionary<ScopedNote>>();

        Assert.Equal(["first", "second"], notes.Keys);
        Assert.Equal("First", notes["first"].Title);
    }

    private sealed class OrderedFrontMatter
    {
        public string? Title { get; set; }

        public int Order { get; set; }
    }

    private void WriteMarkdown(string relativePath, string title, int order = 0)
    {
        var filePath = Path.Combine(_testDir, "contents", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(
            filePath,
            $"""
            ---
            title: {title}
            order: {order}
            ---

            Body of {title}.
            """);
    }

    private KijiBuilder CreateBuilder()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = "contents";
        return builder;
    }

    private IReadOnlyList<string> LoadKeys(Action<MarkdownContentOptions<MarkdownContent<FrontMatter>>> configure)
    {
        var builder = CreateBuilder();
        builder.AddMarkdownContent<FrontMatter>(key: static content => content.FileInfo.Slug, configure: configure);

        var app = builder.Build();
        app.UsePlanningOptions();
        return [.. app.Services.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>().Keys];
    }
}
