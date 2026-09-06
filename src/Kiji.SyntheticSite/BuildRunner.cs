using System.Diagnostics;
using Kiji.Feeds;
using Kiji.Generation;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.SyntheticSite.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.SyntheticSite;

/// <summary>
/// Runs a full site build over the synthetic site and measures it. Each run uses a
/// fresh <see cref="KijiApp"/> so content materialization stays cold, mirroring a
/// real <c>dotnet run</c> build (minus process startup).
/// </summary>
public static class BuildRunner
{
    public static async Task<RunResult> RunOnceAsync(string root, int run, bool collectPhases = false)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        List<PhaseTiming>? phases = null;
        if (collectPhases)
        {
            phases = [];
            BuildPhaseTimer.Observer = (phase, elapsed) => phases.Add(new PhaseTiming(phase, elapsed.TotalMilliseconds));
        }

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await BuildSiteAsync(root);
        }
        finally
        {
            BuildPhaseTimer.Observer = null;
        }

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
            process.PeakWorkingSet64,
            phases);
    }

    /// <summary>
    /// The frozen benchmark site definition, built once. Public so
    /// <c>Kiji.Benchmarks</c> can measure the same workload under BenchmarkDotNet
    /// rather than keep a second copy of it — the definition is what makes numbers
    /// comparable across commits, so there is exactly one.
    /// </summary>
    public static async Task BuildSiteAsync(string root)
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

        builder.AddMarkdownContent<PostFrontMatter>(key: static post => PostSlug.From(post.FileInfo));

        await using var app = builder.Build();
        app.MapDefaultLayout<MainLayout>();
        app.MapPages(typeof(BuildRunner).Assembly);
        app.MapNotFound<NotFoundPage>();

        app.MapRoutes<PostPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
            .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));
        app.MapFeed(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
            .OrderByDescending(static post => post.Value.FrontMatter.CreatedAt)
            .Select(static post => new FeedItem(
                post.Value.FrontMatter.Title ?? string.Empty,
                post.Value.FrontMatter.Description ?? string.Empty,
                post.Value.FrontMatter.CreatedAt ?? DateTimeOffset.UnixEpoch,
                RoutePath: $"blog/{post.Key}/")));
        app.MapSitemap();

        await app.PublishSiteAsync(Path.Combine(root, "dist"));
    }
}
