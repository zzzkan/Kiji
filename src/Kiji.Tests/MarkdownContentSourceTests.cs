using Kiji.Markdown;
using Markdig;
using Markdig.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownContentSourceTests : IDisposable
{
    private readonly string _testDir;

    public MarkdownContentSourceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownContentSourceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testDir, "content"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task OptionsReadingTheirOwnContent_FailsClearlyAndCanRetry()
    {
        WriteMarkdown("post.md", "Post");
        var builder = CreateBuilder();
        var readContent = true;
        builder.Services.AddOptions<MarkdownOptions>()
            .Configure<ContentCollection<MarkdownContent<FrontMatter>>>((options, posts) =>
            {
                if (readContent) { _ = posts.Count; }
            });
        builder.AddMarkdownContent<FrontMatter>();
        await using var site = builder.Build();
        site.UsePlanningOptions();
        var posts = site.ServiceProvider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>();

        var error = Assert.Throws<InvalidOperationException>(() => posts.Count);
        Assert.Contains("recursively", error.Message);
        readContent = false;
        Assert.Single(posts);
    }

    [Theory]
    [InlineData("---\ntitle: Hello", "closing '---'")]
    [InlineData("\n---\ntitle: Hello\n---\nBody", "first line")]
    [InlineData("---\ncreatedAt: \"10:30\"\n---\nBody", "yyyy-MM-dd")]
    public async Task InvalidFrontMatter_FailsLoadingWithSourcePath(string markdown, string reason)
    {
        var path = Path.Combine(_testDir, "content", "post.md");
        await File.WriteAllTextAsync(path, markdown);
        var builder = CreateBuilder();
        builder.AddMarkdownContent<FrontMatter>();
        await using var site = builder.Build();
        site.UsePlanningOptions();

        var error = Assert.Throws<InvalidOperationException>(() => site.ServiceProvider
            .GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>().Count);
        Assert.Contains(path, error.Message);
        Assert.Contains(reason, error.ToString());
    }

    [Fact]
    public void Directory_LimitsTheSourceToThatSubtree()
    {
        WriteMarkdown("posts/first.md", "First");
        WriteMarkdown("notes/alpha.md", "Alpha");

        var posts = LoadKeys(static options => options.Directory = "content/posts");

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
        var builder = CreateBuilder(Path.Combine(_testDir, "site"));
        builder.AddMarkdownContent<FrontMatter>(options => options.Directory = absolute
            ? Path.Combine(_testDir, "content") : "../content");
        await using var app = builder.Build();
        app.UsePlanningOptions();
        Assert.Equal("inside.md", Assert.Single(app.ServiceProvider
            .GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>().Select(content => content.Path)));
    }

    [Fact]
    public void Projection_ProducesTheModel()
    {
        WriteMarkdown("second.md", "Second", order: 2);
        WriteMarkdown("first.md", "First", order: 1);

        var builder = CreateBuilder();
        builder.AddMarkdownContent<OrderedFrontMatter, ProjectedNote>(
            select: static content => new ProjectedNote(Path.GetFileNameWithoutExtension(content.Path), content.FrontMatter.Title));
        var app = builder.Build();
        app.UsePlanningOptions();
        var notes = app.ServiceProvider.GetRequiredService<ContentCollection<ProjectedNote>>();

        Assert.Equal(
            ["first.md", "second.md"],
            notes.Select(note => note.Key + ".md"));
        Assert.Equal(["first", "second"], notes.Select(static note => note.Key));
        Assert.Equal("First", notes.Single(static note => note.Key == "first").Title);
    }

    [Fact]
    public void RawMarkdown_UsesPortableSourceRelativeKeys()
    {
        WriteMarkdown("nested/post.md", "Post");
        var builder = CreateBuilder();
        builder.AddMarkdownContent<FrontMatter>();
        var app = builder.Build();
        app.UsePlanningOptions();

        var content = app.ServiceProvider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>();

        Assert.Equal("nested/post.md", Assert.Single(content).Path);
        Assert.Equal("nested/post.md", Assert.Single(content).Path);
    }

    [Fact]
    public async Task Sources_ShareOneConfiguredPipelineAcrossReloads()
    {
        WriteMarkdown("posts/one.md", "One");
        WriteMarkdown("notes/two.md", "Two");
        var builder = CreateBuilder();
        var pipelines = 0;
        builder.Services.Configure<MarkdownOptions>(options => options.Pipeline.Use(new PipelineCounter(() => pipelines++)));
        builder.AddMarkdownContent<FrontMatter>(options => options.Directory = "content/posts");
        builder.AddMarkdownContent<FrontMatter, ProjectedNote>(
            content => new ProjectedNote(content.Path, content.FrontMatter.Title),
            options => options.Directory = "content/notes");
        await using var app = builder.Build();
        app.UsePlanningOptions();
        var posts = app.ServiceProvider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>();
        var notes = app.ServiceProvider.GetRequiredService<ContentCollection<ProjectedNote>>();
        Assert.Equal("Two", notes["two.md"].Title);
        await posts["one.md"].RenderAsync();
        app.InvalidateContent();
        WriteMarkdown("posts/one.md", "Edited");
        Assert.Equal("Edited", posts["one.md"].FrontMatter.Title);
        await posts["one.md"].RenderAsync();
        Assert.Equal(1, pipelines);
    }

    private sealed class PipelineCounter(Action onBuild) : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline) => onBuild();
        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }
    }

    private sealed record ProjectedNote(string Key, string? Title);

    [Fact]
    public async Task KeyedSources_WithTheSameElementType_ResolveSeparatelyAndReload()
    {
        WriteMarkdown("posts/one.md", "One");
        WriteMarkdown("notes/two.md", "Two");
        var builder = CreateBuilder();
        builder.AddKeyedMarkdownContent<FrontMatter>("posts", options => options.Directory = "content/posts");
        builder.AddKeyedMarkdownContent<FrontMatter>("notes", options => options.Directory = "content/notes");
        builder.AddMarkdownContent<FrontMatter>();
        await using var app = builder.Build();
        app.UsePlanningOptions();
        var posts = app.ServiceProvider.GetRequiredKeyedService<ContentCollection<MarkdownContent<FrontMatter>>>("posts");
        var notes = app.ServiceProvider.GetRequiredKeyedService<ContentCollection<MarkdownContent<FrontMatter>>>("notes");
        Assert.Equal("One", Assert.Single(posts).FrontMatter.Title);
        Assert.Equal("Two", Assert.Single(notes).FrontMatter.Title);
        Assert.Equal(2, app.ServiceProvider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>().Count);
        WriteMarkdown("notes/two.md", "Updated");
        app.InvalidateContent();
        Assert.Equal("Updated", notes["TWO.MD"].FrontMatter.Title);
        Assert.Equal("One", posts["one.md"].FrontMatter.Title);
    }

    [Fact]
    public async Task KeyedProjection_ResolvesItsModels()
    {
        WriteMarkdown("note.md", "Note");
        var builder = CreateBuilder();
        builder.AddKeyedMarkdownContent<FrontMatter, ProjectedNote>("notes",
            content => new ProjectedNote(content.Path, content.FrontMatter.Title));
        await using var app = builder.Build();
        app.UsePlanningOptions();
        var notes = app.ServiceProvider.GetRequiredKeyedService<ContentCollection<ProjectedNote>>("notes");
        Assert.Equal(new ProjectedNote("note.md", "Note"), notes["note.md"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("posts")]
    public void DuplicateElementTypeAndServiceKey_FailsWithRegistrationGuidance(string? serviceKey)
    {
        var builder = CreateBuilder();
        void Register()
        {
            if (serviceKey is null) { builder.AddMarkdownContent<FrontMatter>(); }
            else { builder.AddKeyedMarkdownContent<FrontMatter>(serviceKey); }
        }
        Register();
        var error = Assert.Throws<InvalidOperationException>(Register);
        Assert.Contains("already registered", error.Message, StringComparison.Ordinal);
        Assert.Contains("AddKeyedMarkdownContent", error.Message, StringComparison.Ordinal);
        Assert.Contains("model type", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyServiceKey_IsRejected(string key)
    {
        Assert.Throws<ArgumentException>(() => CreateBuilder().AddKeyedMarkdownContent<FrontMatter>(key));
    }

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
        var builder = CreateBuilder();
        builder.AddMarkdownContent<FrontMatter>();
        builder.AddMarkdownContent<FrontMatter, ProjectedNote>(
            content => new ProjectedNote(content.Path, content.FrontMatter.Title),
            options => options.Directory = directory);
        await using var app = builder.Build();

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
        var filePath = Path.Combine(_testDir, "content", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(
            filePath,
            $"""
            ---
            title: {title}
            {(order == 0 ? string.Empty : $"order: {order}")}
            ---

            Body of {title}.
            """);
    }

    private StaticSiteBuilder CreateBuilder(string? projectDirectory = null)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(projectDirectory ?? _testDir));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        return builder;
    }

    private IReadOnlyList<string> LoadKeys(Action<MarkdownContentOptions> configure)
    {
        var builder = CreateBuilder();
        builder.AddMarkdownContent<FrontMatter>(configure);
        var app = builder.Build();
        app.UsePlanningOptions();
        return [.. app.ServiceProvider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>()
            .Select(static content => Path.GetFileNameWithoutExtension(content.Path))];
    }
}
