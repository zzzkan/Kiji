using BenchmarkDotNet.Attributes;
using Kiji.Generation;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ScopeFingerprintBenchmarks
{
    private string _root = "";
    private ResolvedSitePaths _paths = null!;
    private readonly ContentFileRegistry _registry = new();
    private readonly SiteInfo _site = new() { Name = "Bench", BaseUrl = new Uri("https://example.com/") };

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kiji-scope-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = new ResolvedSitePaths { ContentDirectory = _root, OutputDirectory = _root, StaticDirectory = _root };
        var files = new List<FileInfo>();
        for (var i = 0; i < 1000; i++)
        {
            var file = Path.Combine(_root, $"post-{i}.md");
            File.WriteAllText(file, "body");
            var info = new FileInfo(file);
            _registry.Record(info, BuildFingerprint.HashFile(file));
            files.Add(info);
        }
        _registry.RecordScan(_root, files);
        var baseline = new BaselineIncrementalBuildPlanner(_paths, _registry);
        var candidate = new IncrementalBuildPlanner(_paths, _root, _root, _site, [], [], _registry);
        if (baseline.ContentSetFingerprint("") != candidate.ContentSetFingerprint("")) { throw new InvalidOperationException("Scope hash mismatch."); }
    }
    [Benchmark(Baseline = true)]
    public void Original()
    {
        var planner = new BaselineIncrementalBuildPlanner(_paths, _registry);
        Parallel.For(0, 32, _ => planner.ContentSetFingerprint(""));
    }
    [Benchmark]
    public void SingleComputation()
    {
        var planner = new IncrementalBuildPlanner(_paths, _root, _root, _site, [], [], _registry);
        Parallel.For(0, 32, _ => planner.ContentSetFingerprint(""));
    }
    [GlobalCleanup] public void Cleanup() => Directory.Delete(_root, true);
}
