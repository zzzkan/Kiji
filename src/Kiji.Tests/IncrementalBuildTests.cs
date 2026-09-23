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
        var manifestPath = Path.Combine(root, ".kiji", "cache", "manifest.json");
        var original = await File.ReadAllTextAsync(manifestPath);
        var expected = SnapshotDirectory(Path.Combine(root, "dist"));

        foreach (var damage in new[] { "old-schema", "old-code-identity", "invalid-json", "null-pages", "null-page", "duplicate-page", "missing-dependencies", "empty-parameter-hash", "outside-output", "null-bundle", "outside-bundle" })
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            var pages = json["Pages"]!.AsArray();
            switch (damage)
            {
                case "old-schema": json["SchemaVersion"] = 2; break;
                case "old-code-identity":
                    json["CodeDependencies"] = new System.Text.Json.Nodes.JsonArray("Kiji:" + new string('a', 64));
                    break;
                case "null-bundle": json["HtmlFile"] = null; break;
                case "outside-bundle": json["HtmlFile"] = "html-../outside.bin"; break;
                case "empty-parameter-hash": pages[0]!["ParametersHash"] = ""; break;
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
        var strayTree = Path.Combine(distDir, "manually-added");
        for (var i = 0; i < 32; i++)
        {
            var directory = Path.Combine(strayTree, i.ToString(System.Globalization.CultureInfo.InvariantCulture), "nested");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "stray.txt"), "not produced");
        }

        await BuildAsync(root, RecordRender);

        Assert.Equal(initialRenders, renders);
        Assert.False(File.Exists(stray));
        Assert.False(Directory.Exists(strayTree));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedOutput_UsesStampWithoutChangingCachedBytes(bool preserveStamp)
    {
        var root = CreateSiteRoot("site");
        var renders = 0;
        void Record() { Interlocked.Increment(ref renders); }
        await BuildAsync(root, Record);
        var initialRenders = renders;

        var pagePath = Path.Combine(root, "dist", "md", "stable", "index.html");
        var original = await File.ReadAllBytesAsync(pagePath);
        var stamp = File.GetLastWriteTimeUtc(pagePath);
        var damaged = original.ToArray();
        damaged[0] ^= 1;
        await File.WriteAllBytesAsync(pagePath, damaged);
        File.SetLastWriteTimeUtc(pagePath, preserveStamp ? stamp : stamp.AddMinutes(-1));

        await BuildAsync(root, Record);

        Assert.Equal(initialRenders, renders);
        Assert.Equal(preserveStamp ? damaged : original, await File.ReadAllBytesAsync(pagePath));
        File.Delete(pagePath);
        await BuildAsync(root, Record);
        Assert.Equal(initialRenders, renders);
        Assert.Equal(original, await File.ReadAllBytesAsync(pagePath));
    }

    [Fact]
    public async Task InputChangedDuringRendering_DoesNotReplaceSuccessfulManifest()
    {
        var root = CreateSiteRoot("changed-during-render");
        await BuildAsync(root);
        var manifestPath = Path.Combine(root, ".kiji", "cache", "manifest.json");
        var manifest = await File.ReadAllBytesAsync(manifestPath);
        var changed = 0;
        await using var app = CreateBuildApp(root, () =>
        {
            if (Interlocked.Exchange(ref changed, 1) == 0)
            {
                var input = Path.Combine(root, "contents", "stable.md");
                var stamp = File.GetLastWriteTimeUtc(input);
                File.WriteAllText(input, File.ReadAllText(input).Replace("Stable body.", "Edited body.", StringComparison.Ordinal));
                File.SetLastWriteTimeUtc(input, stamp);
            }
        });
        app.AddBuildInput("test-revision", "changed");
        await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync("dist"));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(manifestPath));
        await BuildAsync(root);
        Assert.Contains("Edited body.", await File.ReadAllTextAsync(Path.Combine(root, "dist", "md", "stable", "index.html")), StringComparison.Ordinal);
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
        var manifestPath = Path.Combine(root, ".kiji", "cache", "manifest.json");
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

    [Fact]
    public async Task CacheOnly_RestoresHtmlInDifferentCheckoutWithoutRendering()
    {
        var original = CreateSiteRoot("original");
        var moved = CreateSiteRoot("moved");
        var renders = 0;
        void Record() { Interlocked.Increment(ref renders); }
        await BuildAsync(original, Record);
        CopyTree(Path.Combine(original, ".kiji"), Path.Combine(moved, ".kiji"));
        var count = renders;
        await BuildAsync(moved, Record);
        Assert.Equal(count, renders);
        AssertDirectoriesIdentical(Path.Combine(original, "dist"), Path.Combine(moved, "dist"));
    }

    [Fact]
    public async Task OutputStamps_AreNotTrustedInAnotherCheckout()
    {
        var original = CreateSiteRoot("stamp-original");
        var moved = CreateSiteRoot("stamp-moved");
        await BuildAsync(original);
        CopyTree(Path.Combine(original, ".kiji"), Path.Combine(moved, ".kiji"));
        CopyTree(Path.Combine(original, "dist"), Path.Combine(moved, "dist"));
        var relative = Path.Combine("dist", "md", "stable", "index.html");
        var bytes = File.ReadAllBytes(Path.Combine(moved, relative));
        bytes[0] ^= 1;
        File.WriteAllBytes(Path.Combine(moved, relative), bytes);
        File.SetLastWriteTimeUtc(Path.Combine(moved, relative), File.GetLastWriteTimeUtc(Path.Combine(original, relative)));
        var renders = 0;
        await BuildAsync(moved, () => Interlocked.Increment(ref renders));
        Assert.Equal(0, renders);
        AssertDirectoriesIdentical(Path.Combine(original, "dist"), Path.Combine(moved, "dist"));
    }

    [Fact]
    public async Task UnchangedBuild_PreservesManifest_AndRestoreOnlyUpdatesStamps()
    {
        var root = CreateSiteRoot("unchanged-cache");
        await BuildAsync(root);
        await WritePostAsync(root, "stable", "Edited", "Updated before the no-change check.");
        await BuildAsync(root);
        var path = Path.Combine(root, ".kiji", "cache", "manifest.json");
        var bytes = await File.ReadAllBytesAsync(path);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        await BuildAsync(root);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var htmlFile = (await ReadCacheManifest(root)).HtmlFile;
        Directory.Delete(Path.Combine(root, "dist"), recursive: true);
        await BuildAsync(root);
        Assert.Equal(htmlFile, (await ReadCacheManifest(root)).HtmlFile);
        bytes = await File.ReadAllBytesAsync(path);
        File.SetLastWriteTimeUtc(path, stamp);
        await BuildAsync(root);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task SameLengthAndTimestamp_ContentChangeIsDetected()
    {
        var root = CreateSiteRoot("stamp");
        await using var app = CreateBuildApp(root);
        await app.PublishAsync("dist");
        var path = Path.Combine(root, "contents", "stable.md");
        var stamp = File.GetLastWriteTimeUtc(path);
        var original = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, original.Replace("Stable body.", "Edited body.", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, stamp);
        await app.PublishAsync("dist");
        Assert.Contains("Edited body.", await File.ReadAllTextAsync(Path.Combine(root, "dist", "md", "stable", "index.html")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MissingAndCorruptCachedHtml_RegeneratesOnlyAffectedPage(bool missing, bool keepOutput)
    {
        var root = CreateSiteRoot("damaged");
        var renders = 0;
        void Record() { Interlocked.Increment(ref renders); }
        await BuildAsync(root, Record);
        var manifest = await ReadCacheManifest(root);
        var stable = manifest.Pages.Single(p => p.RoutePath == "/md/stable/");
        var cache = Path.Combine(root, ".kiji", "cache");
        if (missing)
        {
            stable.HtmlOffset = int.MaxValue;
            await File.WriteAllBytesAsync(Path.Combine(cache, "manifest.json"), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                manifest, Generation.BuildManifestJsonContext.Default.BuildManifest));
        }
        else
        {
            var bundlePath = Path.Combine(cache, manifest.HtmlFile);
            var bytes = await File.ReadAllBytesAsync(bundlePath);
            bytes[stable.HtmlOffset] ^= 1;
            await File.WriteAllBytesAsync(bundlePath, bytes);
        }
        if (!keepOutput) { Directory.Delete(Path.Combine(root, "dist"), recursive: true); }
        var count = renders;
        await BuildAsync(root, Record);
        Assert.Equal(count + 1, renders);
        var repaired = (await ReadCacheManifest(root)).Pages.Single(page => page.RoutePath == stable.RoutePath);
        Assert.Equal(stable.OutputHash, Generation.BuildFingerprint.HashBytes(repaired.Html!.Value.Span));
    }

    [Fact]
    public async Task MissingHtmlBundle_RebuildsAndMatchesCleanOutput()
    {
        var root = CreateSiteRoot("missing-bundle");
        var renders = 0;
        void Record() { Interlocked.Increment(ref renders); }
        await BuildAsync(root, Record);
        var count = renders;
        var manifest = await ReadCacheManifest(root);
        File.Delete(Path.Combine(root, ".kiji", "cache", manifest.HtmlFile));
        await BuildAsync(root, Record);
        Assert.Equal(count * 2, renders);
        var clean = CreateSiteRoot("clean-bundle");
        await BuildAsync(clean);
        AssertDirectoriesIdentical(Path.Combine(clean, "dist"), Path.Combine(root, "dist"));
    }

    [Fact]
    public async Task FailedBuild_KeepsPreviousManifestAndCanRecover()
    {
        var root = CreateSiteRoot("failed");
        await BuildAsync(root);
        var path = Path.Combine(root, ".kiji", "cache", "manifest.json");
        var previous = await File.ReadAllBytesAsync(path);
        await WritePostAsync(root, "stable", "Changed", "Changed body.");
        await Assert.ThrowsAnyAsync<Exception>(() => BuildAsync(root, () => throw new InvalidOperationException("render failed")));
        Assert.Equal(previous, await File.ReadAllBytesAsync(path));
        await BuildAsync(root);
        Assert.Contains("Changed body.", await File.ReadAllTextAsync(Path.Combine(root, "dist", "md", "stable", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulBuild_ReplacesCachedHtmlAndCollectsRetiredBlobs()
    {
        var root = CreateSiteRoot("collection");
        await BuildAsync(root);
        var cache = Path.Combine(root, ".kiji", "cache");
        Directory.CreateDirectory(Path.Combine(cache, "pages"));
        await File.WriteAllTextAsync(Path.Combine(cache, "pages", "retired"), "old cache format");
        var retiredManifest = Path.Combine(cache, "build-manifest.json");
        await File.WriteAllTextAsync(retiredManifest, "obsolete");
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await WritePostAsync(root, "stable", "Revision " + iteration, "New body " + iteration);
            await BuildAsync(root);
            var manifest = await ReadCacheManifest(root);
            Assert.False(Directory.Exists(Path.Combine(cache, "pages")));
            Assert.False(File.Exists(retiredManifest));
            foreach (var page in manifest.Pages)
            {
                Assert.Equal(page.OutputHash, Generation.BuildFingerprint.HashBytes(page.Html!.Value.Span));
                Assert.Equal(page.Html.Value.ToArray(), await File.ReadAllBytesAsync(Path.Combine(root, "dist", page.OutputRelativePath)));
            }
            Assert.Single(Directory.GetFiles(cache, "html-*.bin"));
        }
    }

    [Fact]
    public async Task ExternalContentWithImages_RendersWithoutPortablePageRecipes()
    {
        var root = CreateSiteRoot("external-site");
        var external = CreateSiteRoot("external-content");
        await WritePostAsync(external, "stable", "External image", "![image](source.png)");
        await File.WriteAllBytesAsync(Path.Combine(external, "contents", "source.png"), [1, 2, 3]);
        var renders = 0;
        async Task Build()
        {
            await using var app = CreateBuildApp(root, () => Interlocked.Increment(ref renders));
            app.Paths.ContentDirectory = Path.Combine(external, "contents");
            app.UseImageProcessor(() => new TestSite.PageServices.TrackingImageProcessor());
            await app.PublishAsync("dist");
        }
        await Build();
        var before = SnapshotDirectory(Path.Combine(root, "dist"));
        var firstRenders = renders;
        await Build();
        Assert.True(renders > firstRenders);
        var after = SnapshotDirectory(Path.Combine(root, "dist"));
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, entry) in before) { Assert.Equal(entry.Bytes, after[path].Bytes); }
        var manifest = await ReadCacheManifest(root);
        var imagePage = Assert.Single(manifest.Pages, page => page.AdditionalOutputs.Count > 0);
        Assert.Null(imagePage.ParametersHash);
        Assert.Empty(imagePage.ImageRequests);
        Assert.Equal(0, imagePage.HtmlLength);
    }

    [Fact]
    public async Task DevelopmentImages_DoNotChangePublishCache()
    {
        var root = CreateSiteRoot("dev-images");
        await WritePostAsync(root, "stable", "Image", "![image](source.png)");
        await File.WriteAllBytesAsync(Path.Combine(root, "contents", "source.png"), [1, 2, 3]);
        await using (var publishing = CreateBuildApp(root))
        {
            publishing.UseImageProcessor(() => new TestSite.PageServices.TrackingImageProcessor());
            await publishing.PublishAsync("dist");
        }
        var cache = Path.Combine(root, ".kiji", "cache");
        var before = SnapshotDirectory(cache);
        await using var app = CreateBuildApp(root);
        app.UseImageProcessor(() => new TestSite.PageServices.TrackingImageProcessor());
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            using var client = new HttpClient();
            await client.GetStringAsync(web.Urls.First().TrimEnd('/') + "/md/stable/");
            await web.StopAsync();
        }
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, ".kiji", "dev-site"), "custom.svg", SearchOption.AllDirectories));
        var after = SnapshotDirectory(cache);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, entry) in before)
        {
            Assert.Equal(entry.Bytes, after[path].Bytes);
            Assert.Equal(entry.LastWriteTimeUtc, after[path].LastWriteTimeUtc);
        }
    }

    [Fact]
    public async Task CacheOnly_RepairsImagesWithoutRenderingHtml()
    {
        var root = CreateSiteRoot("images");
        await WritePostAsync(root, "stable", "Image", "![image](source.png)");
        await File.WriteAllBytesAsync(Path.Combine(root, "contents", "source.png"), [1, 2, 3]);
        var processor = new TestSite.PageServices.TrackingImageProcessor();
        var renders = 0;
        void Record() { Interlocked.Increment(ref renders); }
        async Task Build()
        {
            await using var app = CreateBuildApp(root, Record);
            app.UseImageProcessor(() => processor);
            await app.PublishAsync("dist");
        }
        await Build();
        var count = renders;
        var manifest = await ReadCacheManifest(root);
        var output = Assert.Single(manifest.Pages.SelectMany(p => p.AdditionalOutputs));
        await File.WriteAllTextAsync(Path.Combine(root, ".kiji", "cache", "images", output.Hash), "broken");
        Directory.Delete(Path.Combine(root, "dist"), recursive: true);
        await Build();
        Assert.Equal(count, renders);
        Assert.Equal(2, processor.Calls);
        Assert.Equal(output.Hash, Generation.BuildFingerprint.HashFile(Path.Combine(root, "dist", output.RelativePath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedImages_UseStampsAndRestoreVerifiedCache(bool preserveStamp)
    {
        var root = CreateSiteRoot("image-stamps");
        await WritePostAsync(root, "stable", "Image", "![image](source.png)");
        await File.WriteAllBytesAsync(Path.Combine(root, "contents", "source.png"), [1, 2, 3]);
        var processor = new TestSite.PageServices.TrackingImageProcessor();
        async Task Build()
        {
            await using var app = CreateBuildApp(root);
            app.UseImageProcessor(() => processor);
            await app.PublishAsync("dist");
        }
        await Build();
        var output = Assert.Single((await ReadCacheManifest(root)).Pages.SelectMany(page => page.AdditionalOutputs));
        var path = Path.Combine(root, "dist", output.RelativePath);
        var original = File.ReadAllBytes(path);
        var changed = original.ToArray();
        changed[0] ^= 1;
        File.WriteAllBytes(path, changed);
        File.SetLastWriteTimeUtc(path, preserveStamp ? output.Stamp!.Value.LastWriteTimeUtc : output.Stamp!.Value.LastWriteTimeUtc.AddMinutes(-1));
        await Build();
        Assert.Equal(preserveStamp ? changed : original, File.ReadAllBytes(path));
        File.Delete(path);
        await Build();
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ConcurrentPublish_SerializesCacheTransactions()
    {
        var root = CreateSiteRoot("parallel");
        await Task.WhenAll(BuildAsync(root), BuildAsync(root));
        Assert.True((await ReadCacheManifest(root)).IsValid());
        var scratch = CreateSiteRoot("parallel-scratch");
        await BuildAsync(scratch);
        AssertDirectoriesIdentical(Path.Combine(scratch, "dist"), Path.Combine(root, "dist"));
    }

    private static async Task<Generation.BuildManifest> ReadCacheManifest(string root)
    {
        var cache = Path.Combine(root, ".kiji", "cache");
        var manifest = System.Text.Json.JsonSerializer.Deserialize(await File.ReadAllBytesAsync(Path.Combine(cache, "manifest.json")),
            Generation.BuildManifestJsonContext.Default.BuildManifest)!;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(cache, manifest.HtmlFile));
        foreach (var page in manifest.Pages) { page.Html = bytes.AsMemory(page.HtmlOffset, page.HtmlLength); }
        return manifest;
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
        }
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
