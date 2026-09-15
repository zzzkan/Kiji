using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Assets;

namespace Kiji.Benchmarks;

/// <summary>Compares allocation after guarding the old implementation's publication race. Do not use its time as the old implementation's time.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 4, iterationCount: 10, invocationCount: 1)]
public class ImageDuplicateAllocationBenchmarks : IDisposable
{
    private ImageWorkload _workload = null!;
    private readonly BaselineImageProcessor _baseline = new(guardPublication: true);
    private readonly ImageProcessor _candidate = new();
    [Params(640, 2400)] public int Width { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _workload = new ImageWorkload();
        await _workload.SetupAsync(Width, shared: true);
        await _workload.RunAsync(_baseline);
        var expected = _workload.OutputBytes();
        _workload.Reset(true);
        await _workload.RunAsync(_candidate);
        var actual = _workload.OutputBytes();
        if (expected.Count != actual.Count || expected.Any(pair => !actual[pair.Key].AsSpan().SequenceEqual(pair.Value)))
        {
            throw new InvalidOperationException("Image bytes differ.");
        }
    }
    [IterationSetup] public void Reset() => _workload.Reset(true);
    [Benchmark(Baseline = true)] public Task DuplicateWorkWithGuardedPublication() => _workload.RunAsync(_baseline);
    [Benchmark] public Task Deduplicated() => _workload.RunAsync(_candidate);
    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _workload.Dispose();
    }
}
