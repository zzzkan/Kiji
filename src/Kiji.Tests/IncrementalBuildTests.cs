using Kiji.Markdown;
using Kiji.Tests.TestSite;
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

    [Fact]
    public async Task IncrementalBuild_UnknownFileInOutput_FallsBackToFullRebuild()
    {
        var root = CreateSiteRoot("site");
        await BuildAsync(root);

        var strayPath = Path.Combine(root, "dist", "manually-added.txt");
        await File.WriteAllTextAsync(strayPath, "not produced by the build");

        await BuildAsync(root);

        Assert.False(File.Exists(strayPath));
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

        var markdownPosts = builder.AddMarkdownContent<FrontMatter>()
            .WithKey(static post => post.FileInfo.FileNameWithoutExtension);
        var posts = builder.AddContentSource<Post>(static _ => []).WithKey(static post => post.Slug);

        await using var app = builder.Build();
        TestArticleContents.MapSite(app, posts);
        app.MapRoutes<MarkdownPostTestPage, MarkdownContent<FrontMatter>>(
            markdownPosts,
            static post => new { Slug = post.FileInfo.FileNameWithoutExtension });

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
