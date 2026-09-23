using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

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
            options.FileFilter = file => !Path.GetFileNameWithoutExtension(file.Name).StartsWith('_'));

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

        var app = CreateApp();
        app.UseMarkdownContent<OrderedFrontMatter, ProjectedNote>(
            select: static content => new ProjectedNote(Path.GetFileNameWithoutExtension(content.FileInfo.Name), content.FrontMatter.Title));
        app.UsePlanningOptions();
        var notes = app.ServiceProvider.GetRequiredService<ContentDictionary<ProjectedNote>>();

        Assert.Equal(
            ["first.md", "second.md"],
            notes.Keys);
        Assert.Equal(["first", "second"], notes.Values.Select(static note => note.Key));
        Assert.Equal("First", notes.Values.Single(static note => note.Key == "first").Title);
    }

    [Fact]
    public void RawMarkdown_UsesPortableSourceRelativeKeys()
    {
        WriteMarkdown("nested/post.md", "Post");
        var app = CreateApp();
        app.UseMarkdownContent<FrontMatter>();
        app.UsePlanningOptions();

        var content = app.ServiceProvider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>();

        Assert.Equal("nested/post.md", Assert.Single(content.Keys));
    }

    private sealed record ProjectedNote(string Key, string? Title);

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

    private StaticSite CreateApp()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _testDir;
        app.Paths.ContentDirectory = "contents";
        return app;
    }

    private IReadOnlyList<string> LoadKeys(Action<MarkdownOptions> configure)
    {
        var app = CreateApp();
        app.UseMarkdownContent<FrontMatter>(configure);
        app.UsePlanningOptions();
        return [.. app.ServiceProvider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Values.Select(static content => Path.GetFileNameWithoutExtension(content.FileInfo.Name))];
    }
}
