using System.Diagnostics;
using Kiji.Generation;
using Kiji.Hosting;
using Kiji.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kiji.SyntheticSite;

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

    public static async Task BuildSiteAsync(string root)
    {
        var builder = StaticSite.CreateBuilder([], new SiteExecutionPaths(root, Path.Combine(root, "obj", "synthetic")), typeof(BuildRunner).Assembly, runtimeMetadata: new SiteRuntimeMetadata(OutputPath: Path.Combine(root, "dist")));
        SyntheticSiteDefinition.Configure(builder);

        using var host = builder.Build();
        host.Services.GetService<IStartupValidator>()?.Validate();
        await host.Services.GetRequiredService<SiteEngine>().PublishAsync();
    }
}
