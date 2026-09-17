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
/// fresh <see cref="StaticSite"/> so content materialization stays cold, mirroring a
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
        await using var app = StaticSite.Create([]);
        app.Info = new SiteInfo
        {
            BaseUrl = new Uri("https://bench.example.com/"),
            Name = "Kiji Bench",
            Description = "Synthetic benchmark site.",
            Language = "en",
            Author = "bench",
        };
        app.Paths.RootDirectory = root;

        app.UseMarkdownContent<PostFrontMatter>(key: static post => post.FileInfo.FullName);
        app.UseDefaultLayout<MainLayout>();
        app.AddStaticPages(typeof(BuildRunner).Assembly);
        app.UseNotFoundPage<NotFoundPage>();

        app.AddPages<PostPage>(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
            .Select(static post => new { Slug = PostSlug.From(post.Value.FileInfo), ContentKey = post.Key }));
        app.AddRssFeed(static services => services
            .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
            .OrderByDescending(static post => post.Value.FrontMatter.CreatedAt)
            .Select(static post => new FeedItem(
                post.Value.FrontMatter.Title ?? string.Empty,
                post.Value.FrontMatter.Description ?? string.Empty,
                post.Value.FrontMatter.CreatedAt ?? DateTimeOffset.UnixEpoch,
                RoutePath: $"blog/{PostSlug.From(post.Value.FileInfo)}/")));
        app.AddSitemap();

        await app.PublishAsync(Path.Combine(root, "dist"));
    }
}
