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

        var posts = LoadKeys(static options => options.Directory = "contents/posts");

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directory_ResolvesFromProjectDirectoryOrAbsolutePath(bool absolute)
    {
        WriteMarkdown("inside.md", "Inside");
        await using var app = CreateApp(Path.Combine(_testDir, "site"));
        app.UseMarkdownContent<FrontMatter>(options => options.Directory = absolute
            ? Path.Combine(_testDir, "contents") : "../contents");
        app.UsePlanningOptions();
        Assert.Equal("inside.md", Assert.Single(app.ServiceProvider
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>().Keys));
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

    [Theory]
    [InlineData("publish", "publish")]
    [InlineData("publish/nested", "publish")]
    [InlineData("publish", "publish/nested")]
    [InlineData(".kiji/dev-site", null)]
    public async Task Execution_RejectsOutputOverlappingAnyRegisteredSource(string directory, string? output)
    {
        var input = Path.Combine(_testDir, directory, "keep.md");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        await File.WriteAllTextAsync(input, "original");
        await using var app = CreateApp();
        app.UseMarkdownContent<FrontMatter>();
        app.UseMarkdownContent<FrontMatter, ProjectedNote>(
            content => new ProjectedNote(content.FileInfo.Name, content.FrontMatter.Title),
            options => options.Directory = directory);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (output is not null) { await app.PublishAsync(output); }
            else
            {
                var (server, _) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
                await server.DisposeAsync();
            }
        });
        Assert.Contains("overlaps source", error.Message, StringComparison.Ordinal);
        Assert.Equal("original", await File.ReadAllTextAsync(input));
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

    private StaticSite CreateApp(string? projectDirectory = null)
    {
        var app = StaticSite.Create([], new SiteExecutionPaths(projectDirectory ?? _testDir));
        app.Info = TestArticleContents.CreateSiteInfo();
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
