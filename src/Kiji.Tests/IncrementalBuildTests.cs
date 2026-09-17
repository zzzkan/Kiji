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
    public async Task ReusingAppForSameOutput_ReloadsContentAndMatchesFreshBuild()
    {
        var root = CreateSiteRoot("reused");
        await using var app = CreateBuildApp(root);
        await app.PublishAsync(Path.Combine(root, "dist"));
        await WritePostAsync(root, "changing", "Changed", "Freshly loaded content.");
        await app.PublishAsync(Path.Combine(root, "dist"));

        var scratch = CreateSiteRoot("scratch");
        await WritePostAsync(scratch, "changing", "Changed", "Freshly loaded content.");
        await BuildAsync(scratch);
        AssertDirectoriesIdentical(Path.Combine(scratch, "dist"), Path.Combine(root, "dist"));
    }

    [Fact]
    public async Task IncrementalBuild_MalformedManifestFallsBackToFullBuild()
    {
        var renders = 0;
        void RecordRender() { Interlocked.Increment(ref renders); }
        var root = CreateSiteRoot("malformed");
        await BuildAsync(root, RecordRender);
        var manifestPath = Path.Combine(root, ".kiji", "cache", "build-manifest.json");
        var original = await File.ReadAllTextAsync(manifestPath);
        var expected = SnapshotDirectory(Path.Combine(root, "dist"));

        foreach (var damage in new[] { "old-schema", "invalid-json", "null-pages", "null-page", "duplicate-page", "missing-dependencies", "outside-output" })
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            var pages = json["Pages"]!.AsArray();
            switch (damage)
            {
                case "old-schema": json["SchemaVersion"] = 1; break;
                case "null-pages": json["Pages"] = null; break;
                case "null-page": pages[0] = null; break;
                case "duplicate-page": pages.Add(pages[0]!.DeepClone()); break;
                case "missing-dependencies": pages[0]!["Dependencies"] = null; break;
                case "outside-output": pages[0]!["AdditionalOutputs"] = new System.Text.Json.Nodes.JsonArray("../outside"); break;
            }
            var previousRenders = renders;
            await File.WriteAllTextAsync(manifestPath, damage == "invalid-json" ? "{" : json.ToJsonString());
            await BuildAsync(root, RecordRender);
            Assert.True(renders > previousRenders, damage);
            var rebuilt = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
            Assert.Equal(Kiji.Generation.BuildManifest.CurrentSchemaVersion, rebuilt["SchemaVersion"]!.GetValue<int>());
            var actual = SnapshotDirectory(Path.Combine(root, "dist"));
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, entry) in expected)
            {
                Assert.Equal(entry.Bytes, actual[path].Bytes);
            }
        }
    }

    [Fact]
    public async Task SecondBuild_NoChanges_SkipsPagesAndKeepsOutputsIdentical()
    {
        var renders = 0;
        void RecordRender() { Interlocked.Increment(ref renders); }
        var root = CreateSiteRoot("site");
        await BuildAsync(root, RecordRender);

        var distDir = Path.Combine(root, "dist");
        var before = SnapshotDirectory(distDir);
        var initialRenders = renders;
        Assert.True(initialRenders > 0);
        var stray = Path.Combine(distDir, "manually-added.txt");
        await File.WriteAllTextAsync(stray, "not produced");

        await BuildAsync(root, RecordRender);

        Assert.Equal(initialRenders, renders);
        Assert.False(File.Exists(stray));
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

    [Theory]
    [InlineData("notes")]
    [InlineData("contents")]
    public async Task IncrementalBuild_EditInOneContentDirectory_SkipsPagesReadingAnother(string notesDirectory)
    {
        var root = Path.Combine(_testDir, "scoped");
        Directory.CreateDirectory(Path.Combine(root, "contents", "posts"));
        Directory.CreateDirectory(Path.Combine(root, "contents", notesDirectory));
        Directory.CreateDirectory(Path.Combine(root, "static"));

        await WriteMarkdownAsync(root, Path.Combine("posts", "first"), "First Post", "Post body.");
        await WriteMarkdownAsync(root, Path.Combine(notesDirectory, "alpha"), "Alpha Note", "Note body.");

        await BuildScopedAsync(root, notesDirectory);

        var notesIndex = Path.Combine(root, "dist", "notes", "all", "index.html");
        var postPage = Path.Combine(root, "dist", "md", "first", "index.html");
        var notesStampBefore = File.GetLastWriteTimeUtc(notesIndex);
        var manifestPath = Path.Combine(root, ".kiji", "cache", "build-manifest.json");
        var before = System.Text.Json.JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(manifestPath), Kiji.Generation.BuildManifestJsonContext.Default.BuildManifest)!;
        var notesOutput = Path.GetRelativePath(Path.Combine(root, "dist"), notesIndex);

        // Touch content in posts/ only. The notes index enumerates its own collection,
        // so its content-set dependency is scoped to notes/ and still holds.
        await WriteMarkdownAsync(root, Path.Combine("posts", "first"), "First Post", "Edited post body.");
        await BuildScopedAsync(root, notesDirectory);

        Assert.Contains("Edited post body.", await File.ReadAllTextAsync(postPage), StringComparison.Ordinal);
        Assert.Equal(notesStampBefore, File.GetLastWriteTimeUtc(notesIndex));
        var after = System.Text.Json.JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(manifestPath), Kiji.Generation.BuildManifestJsonContext.Default.BuildManifest)!;
        // An unchanged HTML file alone cannot prove the scope stayed unchanged:
        // rendering can produce identical HTML and skip the write.
        Assert.Equal(
            before.Pages.Single(page => page.OutputRelativePath == notesOutput).Dependencies,
            after.Pages.Single(page => page.OutputRelativePath == notesOutput).Dependencies);

        // Editing notes/ must still re-render it, or the scoping would be unsound.
        await WriteMarkdownAsync(root, Path.Combine(notesDirectory, "beta"), "Beta Note", "Second note.");
        await BuildScopedAsync(root, notesDirectory);

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
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = root;
        app.Paths.ContentDirectory = "contents";
        app.Paths.StaticDirectory = "static";

        app.UseMarkdownContent<FrontMatter>();
        app.UseContentSource<Post>(static _ => []);
        app.UseDefaultLayout<MainLayout>();
        app.AddStaticPages(typeof(TestArticleContents).Assembly);
        app.UseNotFoundPage<NotFoundPage>();

        app.AddPages<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));

        app.AddPages<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));
        app.AddPages<RelatedPostsTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));

        await app.PublishAsync(Path.Combine(root, "dist"));
    }

    private static async Task BuildScopedAsync(string root, string notesDirectory)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = root;
        app.Paths.ContentDirectory = "contents";
        app.Paths.StaticDirectory = "static";

        app.UseMarkdownContent<FrontMatter>(static options => options.Directory = "posts");
        app.UseMarkdownContent<FrontMatter, ScopedNote>(
            select: ScopedNote.Create,
            configure: options => options.Directory = notesDirectory);
        app.UseContentSource<Post>(static _ => []);
        app.UseDefaultLayout<MainLayout>();
        app.AddStaticPages(typeof(TestArticleContents).Assembly);
        app.UseNotFoundPage<NotFoundPage>();
        app.AddPages<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        app.AddPages<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));
        app.AddPages<ScopedNotesIndexPage>(static _ => [new { Kind = "all" }]);

        await app.PublishAsync(Path.Combine(root, "dist"));
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

    private static async Task BuildAsync(string root, Action? onRender = null)
    {
        await using var app = CreateBuildApp(root, onRender);
        await app.PublishAsync(Path.Combine(root, "dist"));
    }

    private static StaticSite CreateBuildApp(string root, Action? onRender = null)
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = root;
        app.Paths.ContentDirectory = "contents";
        app.Paths.StaticDirectory = "static";

        app.UseMarkdownContent<FrontMatter>(options => options.AddHtmlPostProcessor(html => { onRender?.Invoke(); return html; }));
        app.UseContentSource<Post>(static _ => []);
        TestArticleContents.MapSite(app);
        app.AddPages<MarkdownPostTestPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(static post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));

        return app;
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
