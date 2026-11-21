using Kiji.Generation;
using Kiji.Images.Skia;
using Kiji.Markdown;
using Kiji.Paths;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownContentsBuilderTests : IDisposable
{
    private readonly MarkdownProcessor _markdownProcessor;
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _outputDir;

    public MarkdownContentsBuilderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MarkdownContentsBuilderTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "content");
        _outputDir = Path.Combine(_testDir, "output");
        Directory.CreateDirectory(_contentsDir);
        Directory.CreateDirectory(_outputDir);
        _markdownProcessor = new MarkdownProcessor(new ResolvedSitePaths
        {
            OutputDirectory = _outputDir,
        }, new SiteExecutionPaths(_testDir), TestArticleContents.CreateSiteInfo(), new ContentPath([_testDir]), new ImageProcessor());
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
            CreateValidMarkdown("Test Post", new DateTime(2024, 1, 15), "# Heading\n\n**Test content.**"));

        var renders = 0;
        var builder = new MarkdownContentsBuilder<FrontMatter>(_contentsDir, (content, ct) =>
        {
            renders++;
            return RenderAsync(content, ct);
        });
        var contents = builder.Build();
        Assert.Equal(0, renders);

        var item = Assert.Single(contents);
        Assert.Equal("test-post", Path.GetFileNameWithoutExtension(item.Path));
        Assert.Equal("Test Post", item.FrontMatter.Title);
        var html = await item.RenderAsync();
        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Test content.</strong>", html, StringComparison.Ordinal);
        Assert.Equal(1, renders);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outputDir));
    }

    [Fact]
    public async Task ProcessAsync_NewBuildPicksUpUpdatedMarkdownWhileExistingContentsRemainStable()
    {
        var markdownPath = Path.Combine(_contentsDir, "update-test.md");
        await File.WriteAllTextAsync(markdownPath, CreateValidMarkdown("Update Test", new DateTime(2024, 1, 15), "Original content."));

        var firstContents = CreateBuilder().Build();
        var firstItem = Assert.Single(firstContents, static item => Path.GetFileNameWithoutExtension(item.Path) == "update-test");
        Assert.Contains("Original content.", await firstItem.RenderAsync(), StringComparison.Ordinal);

        await File.WriteAllTextAsync(markdownPath, CreateValidMarkdown("Update Test", new DateTime(2024, 1, 15), "Updated content."));

        var secondContents = CreateBuilder().Build();
        var secondItem = Assert.Single(secondContents, static item => Path.GetFileNameWithoutExtension(item.Path) == "update-test");

        Assert.Contains("Original content.", await firstItem.RenderAsync(), StringComparison.Ordinal);
        Assert.Contains("Updated content.", await secondItem.RenderAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_MultipleFiles_PreservesSortedMetadata()
    {
        var expected = new List<string>(3);
        for (var i = 0; i < 3; i++)
        {
            var fileName = $"post-{i:D3}.md";
            expected.Add($"post-{i:D3}");
            await File.WriteAllTextAsync(
                Path.Combine(_contentsDir, fileName),
                CreateValidMarkdown($"Post {i}", new DateTime(2024, 1, 1).AddDays(i)));
        }

        var contents = CreateBuilder().Build();

        Assert.Equal(expected, contents.Select(static item => Path.GetFileNameWithoutExtension(item.Path)));
        Assert.All(contents, static item => Assert.NotNull(item.FrontMatter.Title));
    }

    [Fact]
    public async Task Build_InvalidFrontMatter_SurfacesFirstFailingFileUnwrapped()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "aaa-broken.md"),
            "---\ntitle: [broken\n---\nbody");
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "bbb-valid.md"),
            CreateValidMarkdown("Valid", new DateTime(2024, 1, 15)));

        var exception = Assert.Throws<InvalidOperationException>(() => CreateBuilder().Build());

        Assert.Contains("aaa-broken.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_WithSourceCache_ReusesUnchangedSourcesAndObservesChanges()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "stable.md"),
            CreateValidMarkdown("Stable", new DateTime(2024, 1, 10)));
        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "changing.md"),
            CreateValidMarkdown("Changing", new DateTime(2024, 1, 15)));

        var cache = new MarkdownSourceCache<FrontMatter>();
        var builder = new MarkdownContentsBuilder<FrontMatter>(
            _contentsDir,
            RenderAsync,
            static () => new MarkdownOptions().CreateDeserializer(),
            cache);

        var first = builder.Build();
        var second = builder.Build();

        Assert.Same(
            Assert.Single(first, static item => Path.GetFileNameWithoutExtension(item.Path) == "changing").FrontMatter,
            Assert.Single(second, static item => Path.GetFileNameWithoutExtension(item.Path) == "changing").FrontMatter);

        await File.WriteAllTextAsync(
            Path.Combine(_contentsDir, "changing.md"),
            CreateValidMarkdown("Changing Updated Title", new DateTime(2024, 1, 15)));

        var third = builder.Build();

        Assert.Equal(
            "Changing Updated Title",
            Assert.Single(third, static item => Path.GetFileNameWithoutExtension(item.Path) == "changing").FrontMatter.Title);
        Assert.Same(
            Assert.Single(first, static item => Path.GetFileNameWithoutExtension(item.Path) == "stable").FrontMatter,
            Assert.Single(third, static item => Path.GetFileNameWithoutExtension(item.Path) == "stable").FrontMatter);
    }

    [Theory]
    [InlineData("post.md", "Post.md")]
    [InlineData("notes/2024.md", @"notes\2024.md")]
    public async Task Build_ReportsBothFilesThatShareAContentId(string first, string second)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Backslashes are Windows path separators.");
        var paths = new[] { Path.Combine(_contentsDir, first), Path.Combine(_contentsDir, second) };
        Directory.CreateDirectory(Path.GetDirectoryName(paths[0])!);
        await File.WriteAllTextAsync(paths[0], CreateValidMarkdown("First", new DateTime(2024, 1, 15)));
        Assert.SkipWhen(File.Exists(paths[1]), "Requires a case-sensitive filesystem.");
        await File.WriteAllTextAsync(paths[1], CreateValidMarkdown("Second", new DateTime(2024, 1, 15)));

        var exception = Assert.Throws<InvalidOperationException>(() => CreateBuilder().Build());

        Assert.Contains(paths[0], exception.Message, StringComparison.Ordinal);
        Assert.Contains(paths[1], exception.Message, StringComparison.Ordinal);
        Assert.Contains("content ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_FilteredOutFileDoesNotConflictWithALoadedContentId()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Backslashes are Windows path separators.");
        Directory.CreateDirectory(Path.Combine(_contentsDir, "notes"));
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "notes", "2024.md"), CreateValidMarkdown("Kept", new DateTime(2024, 1, 15)));
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, @"notes\2024.md"), CreateValidMarkdown("Skipped", new DateTime(2024, 1, 15)));
        var builder = new MarkdownContentsBuilder<FrontMatter>(_contentsDir, RenderAsync, filter: static file => !file.Name.Contains('\\'));

        Assert.Equal("Kept", Assert.Single(builder.Build()).FrontMatter.Title);
    }

    [Fact]
    public async Task Build_CallsTheFileFilterForOneFileAtATimeInPathOrder()
    {
        for (var index = 0; index < 64; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_contentsDir, $"post-{index:00}.md"),
                CreateValidMarkdown("Post", new DateTime(2024, 1, 15)));
        }
        var active = 0;
        var overlapped = false;
        var seen = new List<string>();
        var builder = new MarkdownContentsBuilder<FrontMatter>(_contentsDir, RenderAsync, filter: file =>
        {
            if (Interlocked.Increment(ref active) != 1) { overlapped = true; }
            Thread.SpinWait(20_000);
            seen.Add(file.Name);
            Interlocked.Decrement(ref active);
            return !file.Name.EndsWith("0.md", StringComparison.Ordinal);
        });

        var contents = builder.Build();

        Assert.False(overlapped);
        Assert.Equal(seen.Order(StringComparer.Ordinal), seen);
        Assert.Equal(64, seen.Count);
        Assert.Equal(57, contents.Count);
        Assert.DoesNotContain(contents, static item => item.Path.EndsWith("0.md", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("file", false)]
    [InlineData("directory", false)]
    [InlineData("nested-directory", false)]
    [InlineData("loop", false)]
    [InlineData("root", true)]
    public async Task Build_RejectsLinksBelowTheContentRootInsteadOfFollowingThem(string link, bool loads)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires privileges on Windows.");
        var outside = Path.Combine(_testDir, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "Outside the content root.");
        await File.WriteAllTextAsync(Path.Combine(outside, "inner.md"), CreateValidMarkdown("Inner", new DateTime(2024, 1, 15)));
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "real.md"), CreateValidMarkdown("Real", new DateTime(2024, 1, 15)));
        Directory.CreateDirectory(Path.Combine(_contentsDir, "posts"));
        var contents = _contentsDir;
        switch (link)
        {
            case "file": File.CreateSymbolicLink(Path.Combine(_contentsDir, "leak.md"), Path.Combine(outside, "secret.txt")); break;
            case "directory": Directory.CreateSymbolicLink(Path.Combine(_contentsDir, "linked"), outside); break;
            case "nested-directory": Directory.CreateSymbolicLink(Path.Combine(_contentsDir, "posts", "linked"), outside); break;
            case "loop": Directory.CreateSymbolicLink(Path.Combine(_contentsDir, "posts", "loop"), _contentsDir); break;
            default:
                contents = Path.Combine(_testDir, "linked-content");
                Directory.CreateSymbolicLink(contents, _contentsDir);
                break;
        }
        var builder = new MarkdownContentsBuilder<FrontMatter>(contents, RenderAsync);

        if (loads)
        {
            Assert.Equal("real.md", Assert.Single(builder.Build()).Path);
            return;
        }
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("symbolic links", exception.Message, StringComparison.Ordinal);
    }

    private MarkdownContentsBuilder<FrontMatter> CreateBuilder()
    {
        return new MarkdownContentsBuilder<FrontMatter>(_contentsDir, RenderAsync);
    }

    private Task<string> RenderAsync(MarkdownContent<FrontMatter> content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        return _markdownProcessor.ProcessBodyAsync(content.SourcePath, content.Body, cancellationToken);
    }

    private static string CreateValidMarkdown(string title, DateTime createdAt, string content = "Test content.")
    {
        return $$"""
            ---
            title: {{title}}
            createdAt: {{createdAt:yyyy-MM-dd}}
            ---

            {{content}}
            """;
    }
}
