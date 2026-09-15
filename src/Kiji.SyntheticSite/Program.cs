using System.Globalization;
using System.Text.Json;
using Kiji.SyntheticSite;

var options = HarnessOptions.Parse(args);

var ownsRoot = options.Root is null;
var root = options.Root ?? Path.Combine(Path.GetTempPath(), $"kiji-bench-{Guid.NewGuid():N}");

try
{
    if (!Directory.Exists(Path.Combine(root, "contents")))
    {
        Console.WriteLine($"Generating synthetic site: {options.Pages} pages (images: {options.Images}) at {root}");
        await SyntheticSiteWriter.WriteAsync(root, options.Pages, options.Images);
    }
    else
    {
        Console.WriteLine($"Reusing existing synthetic site at {root}");
    }

    var runs = new List<RunResult>(options.Runs);
    for (var run = 1; run <= options.Runs; run++)
    {
        if (options.FullEachRun)
        {
            // Removing the manifest forces a from-scratch build every run.
            var kijiDir = Path.Combine(root, ".kiji");
            if (Directory.Exists(kijiDir))
            {
                Directory.Delete(kijiDir, recursive: true);
            }
        }

        var result = await BuildRunner.RunOnceAsync(root, run, options.Phases);
        runs.Add(result);
        Report($"run {run} ({(options.FullEachRun || run == 1 ? "full" : "no-change")})", result);
    }

    // One-post-touched rebuild: the headline incremental metric.
    var mutatedPost = Path.Combine(root, "contents", "post-00000", "index.md");
    var originalPost = await File.ReadAllTextAsync(mutatedPost);
    RunResult mutationRun;
    try
    {
        await File.WriteAllTextAsync(mutatedPost, originalPost + "\n\nEdited for the incremental measurement.\n");
        mutationRun = await BuildRunner.RunOnceAsync(root, options.Runs + 1, options.Phases);
    }
    finally
    {
        await File.WriteAllTextAsync(mutatedPost, originalPost);
    }
    Report("rebuild after editing 1 post", mutationRun);

    static void Report(string label, RunResult result)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label,-32}: {result.ElapsedMs,9:F1} ms | alloc {result.AllocatedBytes / (1024.0 * 1024.0),8:F1} MB | GC {result.Gen0Collections}/{result.Gen1Collections}/{result.Gen2Collections} | peak WS {result.PeakWorkingSetBytes / (1024.0 * 1024.0),7:F1} MB"));

        if (result.Phases is not { Count: > 0 } phases)
        {
            return;
        }

        // Phases are reported on one line so a run stays greppable as a unit.
        var breakdown = string.Join(
            " ",
            phases.Select(static phase => string.Create(CultureInfo.InvariantCulture, $"{phase.Phase}={phase.ElapsedMs:F0}")));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{"  phases",-32}: {breakdown} | sum {phases.Where(static phase => !phase.Phase.Contains('.', StringComparison.Ordinal)).Sum(static phase => phase.ElapsedMs):F0} ms"));
    }

    var sortedElapsed = runs.Select(static r => r.ElapsedMs).Order().ToArray();
    var median = sortedElapsed[sortedElapsed.Length / 2];

    var report = new BenchReport(
        DateTimeOffset.UtcNow,
        options.Pages,
        options.Images,
        Environment.Version.ToString(),
        Environment.ProcessorCount,
        System.Runtime.GCSettings.IsServerGC,
        runs,
        median,
        mutationRun);

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"median: {median:F1} ms over {options.Runs} runs"));

    if (options.OutJsonPath is not null)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutJsonPath))!);
        await File.WriteAllTextAsync(options.OutJsonPath, json);
        Console.WriteLine($"report: {Path.GetFullPath(options.OutJsonPath)}");
    }

    return 0;
}
finally
{
    if (ownsRoot && Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}
