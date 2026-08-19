using Kiji.Markdown;
using Kiji.Tests.TestSite.Pages;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// End-to-end guarantees of the incremental build: outputs are always byte-identical
/// to a from-scratch build, unchanged pages are genuinely skipped, and every
/// ambiguous situation falls back to a full rebuild.
/// </summary>
public sealed class IncrementalBuildTests : IDisposable
{
    private readonly string _testDir;

    public IncrementalBuildTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"IncrementalBuildTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task SecondBuild_NoChanges_SkipsPagesAndKeepsOutputsIdentical()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var distDir = Path.Combine(root, "dist");
        var before = SnapshotDirectory(distDir);

        await BuildAsync(root);

        var after = SnapshotDirectory(distDir);
        Assert.Equal(before.Keys.Order(StringComparer.OrdinalIgnoreCase), after.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (path, (bytes, lastWrite)) in before)
        {
            Assert.Equal(bytes, after[path].Bytes);

            // Page outputs must not have been rewritten (the whole point of skipping).
            if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(lastWrite, after[path].LastWriteTimeUtc);
            }
        }
    }

    [Fact]
    public async Task IncrementalBuild_ChangedPost_MatchesFromScratchBuild()
    {
        var incrementalRoot = CreateSiteRoot("incremental");
        await BuildAsync(incrementalRoot);

        var unchangedPage = Path.Combine(incrementalRoot, "dist", "md", "stable", "index.html");
        var unchangedStampBefore = File.GetLastWriteTimeUtc(unchangedPage);

        // Mutate one post, then rebuild incrementally.
        await WritePostAsync(incrementalRoot, "changing", "Changing Reloaded", "Updated body text.");
        await BuildAsync(incrementalRoot);

        // The changed page reflects the edit; the untouched page was not rewritten.
        var changedHtml = await File.ReadAllTextAsync(Path.Combine(incrementalRoot, "dist", "md", "changing", "index.html"));
        Assert.Contains("Updated body text.", changedHtml, StringComparison.Ordinal);
        Assert.Equal(unchangedStampBefore, File.GetLastWriteTimeUtc(unchangedPage));

        // A from-scratch build over the same final content produces identical bytes.
        var scratchRoot = CreateSiteRoot("scratch");
        await WritePostAsync(scratchRoot, "changing", "Changing Reloaded", "Updated body text.");
        await BuildAsync(scratchRoot);

        AssertDirectoriesIdentical(Path.Combine(scratchRoot, "dist"), Path.Combine(incrementalRoot, "dist"));
    }

    [Fact]
    public async Task IncrementalBuild_RemovedPost_DeletesOrphanedOutput()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var removedPage = Path.Combine(root, "dist", "md", "changing", "index.html");
        Assert.True(File.Exists(removedPage));

        File.Delete(Path.Combine(root, "contents", "changing.md"));
        await BuildAsync(root);

        Assert.False(File.Exists(removedPage));
        Assert.False(Directory.Exists(Path.Combine(root, "dist", "md", "changing")));
        Assert.True(File.Exists(Path.Combine(root, "dist", "md", "stable", "index.html")));
    }

    /// <summary>
    /// A file no build produced is something to delete, not a reason to redo the work.
    /// The output directory is reconciled against what the build produced, so the stray
    /// file goes and every page that could be proven unchanged still gets skipped.
    /// </summary>
    [Fact]
    public async Task IncrementalBuild_UnknownFileInOutput_RemovesItWithoutReRenderingPages()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var page = Path.Combine(root, "dist", "md", "stable", "index.html");
        var pageStampBefore = File.GetLastWriteTimeUtc(page);

        var strayPath = Path.Combine(root, "dist", "manually-added.txt");
        await File.WriteAllTextAsync(strayPath, "not produced by the build");

        await BuildAsync(root);

        Assert.False(File.Exists(strayPath));
        Assert.True(File.Exists(page));
        Assert.Equal(pageStampBefore, File.GetLastWriteTimeUtc(page));
    }

    /// <summary>
    /// A stray file sitting where a real page belongs is not left alone: the page's
    /// recorded output hash no longer matches, so the page re-renders over it.
    /// </summary>
    [Fact]
    public async Task IncrementalBuild_StrayFileCollidingWithAPageOutput_IsOverwritten()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var page = Path.Combine(root, "dist", "md", "stable", "index.html");
        var original = await File.ReadAllBytesAsync(page);

        // Same length as a real output would never be; content certainly is not.
        await File.WriteAllTextAsync(page, "stray");

        await BuildAsync(root);

        Assert.Equal(original, await File.ReadAllBytesAsync(page));
    }

    [Fact]
    public async Task IncrementalBuild_TamperedPageOutput_ReRendersPage()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var pagePath = Path.Combine(root, "dist", "md", "stable", "index.html");
        var original = await File.ReadAllBytesAsync(pagePath);
        await File.WriteAllTextAsync(pagePath, "tampered");

        await BuildAsync(root);

        Assert.Equal(original, await File.ReadAllBytesAsync(pagePath));
    }

    [Fact]
    public async Task IncrementalBuild_StaticFiles_CopiesOnlyChanges()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var existingDest = Path.Combine(root, "dist", "app.css");
        var stampBefore = File.GetLastWriteTimeUtc(existingDest);

        await File.WriteAllTextAsync(Path.Combine(root, "static", "extra.txt"), "new static file");
        await BuildAsync(root);

        Assert.Equal(stampBefore, File.GetLastWriteTimeUtc(existingDest));
        Assert.True(File.Exists(Path.Combine(root, "dist", "extra.txt")));
    }

    [Fact]
    public async Task IncrementalBuild_EditInOneContentDirectory_SkipsPagesReadingAnother()
    {
        var root = Path.Combine(_testDir, "scoped");
        Directory.CreateDirectory(Path.Combine(root, "contents", "posts"));
        Directory.CreateDirectory(Path.Combine(root, "contents", "notes"));
        Directory.CreateDirectory(Path.Combine(root, "static"));

        await WriteMarkdownAsync(root, Path.Combine("posts", "first"), "First Post", "Post body.");
        await WriteMarkdownAsync(root, Path.Combine("notes", "alpha"), "Alpha Note", "Note body.");

        await BuildScopedAsync(root);

        var notesIndex = Path.Combine(root, "dist", "notes", "all", "index.html");
        var postPage = Path.Combine(root, "dist", "md", "first", "index.html");
        var notesStampBefore = File.GetLastWriteTimeUtc(notesIndex);

        // Touch content in posts/ only. The notes index enumerates its own collection,
        // so its content-set dependency is scoped to notes/ and still holds.
        await WriteMarkdownAsync(root, Path.Combine("posts", "first"), "First Post", "Edited post body.");
        await BuildScopedAsync(root);

        Assert.Contains("Edited post body.", await File.ReadAllTextAsync(postPage), StringComparison.Ordinal);
        Assert.Equal(notesStampBefore, File.GetLastWriteTimeUtc(notesIndex));

        // Editing notes/ must still re-render it, or the scoping would be unsound.
        await WriteMarkdownAsync(root, Path.Combine("notes", "beta"), "Beta Note", "Second note.");
        await BuildScopedAsync(root);

        Assert.Contains("beta: Beta Note", await File.ReadAllTextAsync(notesIndex), StringComparison.Ordinal);
    }

    /// <summary>
    /// A page that computes derived data by enumerating the dictionary (related posts, a tag
    /// list) depends on the whole content set by construction. Editing a *different* post
    /// must therefore re-render it — this is the guarantee that makes "just compute it in
    /// the page" the recommended shape for derived data.
    /// </summary>
    [Fact]
    public async Task IncrementalBuild_PageEnumeratingTheDictionary_ReRendersWhenAnotherPostChanges()
    {
        var root = CreateSiteRoot("related");
        await BuildRelatedAsync(root);

        var relatedPage = Path.Combine(root, "dist", "related", "stable", "index.html");
        var detailPage = Path.Combine(root, "dist", "md", "stable", "index.html");
        var relatedStampBefore = File.GetLastWriteTimeUtc(relatedPage);
        var detailStampBefore = File.GetLastWriteTimeUtc(detailPage);

        // Edit a different post than the one these pages are for.
        await WritePostAsync(root, "changing", "Changing Renamed", "Body.");
        await BuildRelatedAsync(root);

        // The related list names every other post, so it must reflect the rename.
        Assert.Contains("Changing Renamed", await File.ReadAllTextAsync(relatedPage), StringComparison.Ordinal);
        Assert.NotEqual(relatedStampBefore, File.GetLastWriteTimeUtc(relatedPage));

        // The plain detail page reads only its own file, so it is still skipped.
        Assert.Equal(detailStampBefore, File.GetLastWriteTimeUtc(detailPage));
    }

    private static async Task BuildRelatedAsync(string root)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = root;
        builder.Paths.Content = "contents";
        builder.Paths.Static = "static";
        builder.Paths.Output = "dist";

        builder.AddMarkdownContent<FrontMatter>(key: static post => post.FileInfo.Slug);
        builder.AddContentSource<Post>(static _ => [], static post => post.Slug);

        await using var app = builder.Build();
        app.MapDefaultLayout<MainLayout>();
        app.MapPages(typeof(TestArticleContents).Assembly);
        app.MapNotFound<NotFoundPage>();

        app.MapRoutes<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        app.MapRoutes<TagsPage>(static _ => []);
        app.MapRoutes<MirrorPostPage>(static _ => []);
        app.MapRoutes<ScopedNotesIndexPage>(static _ => []);

        app.MapRoutes<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));
        app.MapRoutes<RelatedPostsTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));

        await app.BuildSiteAsync();
    }

    private static async Task BuildScopedAsync(string root)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = root;
        builder.Paths.Content = "contents";
        builder.Paths.Static = "static";
        builder.Paths.Output = "dist";

        builder.AddMarkdownContent<FrontMatter>(
            key: static post => post.FileInfo.Slug,
            configure: static options => options.Directory = "posts");
        builder.AddMarkdownContent<FrontMatter, ScopedNote>(
            select: ScopedNote.Create,
            key: static note => note.Key,
            configure: static options => options.Directory = "notes");
        builder.AddContentSource<Post>(static _ => [], static post => post.Slug);

        await using var app = builder.Build();
        app.MapDefaultLayout<MainLayout>();
        app.MapPages(typeof(TestArticleContents).Assembly);
        app.MapNotFound<NotFoundPage>();
        app.MapRoutes<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        app.MapRoutes<TagsPage>(static _ => []);
        app.MapRoutes<MirrorPostPage>(static _ => []);
        app.MapRoutes<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));
        app.MapRoutes<ScopedNotesIndexPage>(static _ => [new { Kind = "all" }]);
        app.MapRoutes<RelatedPostsTestPage>(static _ => []);

        await app.BuildSiteAsync();
    }

    private static async Task WriteMarkdownAsync(string root, string relativePath, string title, string body)
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, "contents", $"{relativePath}.md"),
            $"""
            ---
            title: {title}
            createdAt: 2026-03-18
            ---

            {body}
            """);
    }

    private string CreateSiteRoot(string name)
    {
        var root = Path.Combine(_testDir, name);
        Directory.CreateDirectory(Path.Combine(root, "contents"));
        Directory.CreateDirectory(Path.Combine(root, "static"));

        File.WriteAllText(Path.Combine(root, "static", "app.css"), "body { margin: 0; }");
        WritePostAsync(root, "stable", "Stable Post", "Stable body.").GetAwaiter().GetResult();
        WritePostAsync(root, "changing", "Changing Post", "Original body.").GetAwaiter().GetResult();
        return root;
    }

    private static async Task WritePostAsync(string root, string slug, string title, string body)
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, "contents", $"{slug}.md"),
            $"""
            ---
            title: {title}
            createdAt: 2026-03-18
            ---

            {body}
            """);
    }

    private static async Task BuildAsync(string root)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = root;
        builder.Paths.Content = "contents";
        builder.Paths.Static = "static";
        builder.Paths.Output = "dist";

        builder.AddMarkdownContent<FrontMatter>(key: static post => post.FileInfo.Slug);
        builder.AddContentSource<Post>(static _ => [], static post => post.Slug);

        await using var app = builder.Build();
        TestArticleContents.MapSite(app);
        app.MapRoutes<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));

        await app.BuildSiteAsync();
    }

    private static Dictionary<string, (byte[] Bytes, DateTime LastWriteTimeUtc)> SnapshotDirectory(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(directory, file),
            file => (File.ReadAllBytes(file), File.GetLastWriteTimeUtc(file)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertDirectoriesIdentical(string expectedDir, string actualDir)
    {
        var expected = SnapshotDirectory(expectedDir);
        var actual = SnapshotDirectory(actualDir);

        Assert.Equal(
            expected.Keys.Order(StringComparer.OrdinalIgnoreCase),
            actual.Keys.Order(StringComparer.OrdinalIgnoreCase));

        foreach (var (path, (bytes, _)) in expected)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(actual[path].Bytes), $"Output file '{path}' differs from the from-scratch build.");
        }
    }
}
