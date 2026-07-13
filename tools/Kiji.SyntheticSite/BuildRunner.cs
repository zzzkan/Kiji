using System.Diagnostics;
using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.SyntheticSite.Pages;

namespace Kiji.SyntheticSite;

/// <summary>
/// Runs a full site build over the synthetic site and measures it. Each run uses a
/// fresh <see cref="KijiApp"/> so content materialization stays cold, mirroring a
/// real <c>dotnet run</c> build (minus process startup).
/// </summary>
public static class BuildRunner
{
    public static async Task<RunResult> RunOnceAsync(string root, int run)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        await BuildSiteAsync(root);

        stopwatch.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);

        using var process = Process.GetCurrentProcess();
        return new RunResult(
            run,
            stopwatch.Elapsed.TotalMilliseconds,
            allocatedAfter - allocatedBefore,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            process.PeakWorkingSet64);
    }

    private static async Task BuildSiteAsync(string root)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = new SiteInfo
        {
            BaseUrl = new Uri("https://bench.example.com/"),
            Name = "Kiji Bench",
            Description = "Synthetic benchmark site.",
            Language = "en",
            Author = "bench",
        };
        builder.Paths.Root = root;

        var posts = builder.AddMarkdownContent<PostFrontMatter>()
            .WithKey(static post => PostSlug.From(post.FileInfo))
            .OrderByDescending(static post => post.FrontMatter.CreatedAt);

        await using var app = builder.Build();
        app.MapDefaultLayout<MainLayout>();
        app.MapPages(typeof(BuildRunner).Assembly);
        app.MapNotFound<NotFoundPage>();
        app.MapContent<PostPage, MarkdownContent<PostFrontMatter>>(
            posts,
            static post => new { Slug = PostSlug.From(post.FileInfo) },
            static post => post.FrontMatter.CreatedAt);
        app.MapFeed(posts, static post => new FeedItem(
            post.FrontMatter.Title ?? string.Empty,
            post.FrontMatter.Description ?? string.Empty,
            post.FrontMatter.CreatedAt ?? DateTimeOffset.UnixEpoch));
        app.MapSitemap();

        await app.BuildSiteAsync();
    }
}
