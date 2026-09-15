using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Assets;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class ImageProcessingBenchmarks : IDisposable
{
    private ImageWorkload _workload = null!;
    private readonly ImageProcessor _candidate = new();
    private readonly BaselineImageProcessor _baseline = new();
    [Params(640, 2400)] public int Width { get; set; }
    [Params(false, true)] public bool Shared { get; set; }
    [Params(false, true)] public bool Cold { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _workload = new ImageWorkload();
        await _workload.SetupAsync(Width, Shared);
        // The original has a shared-cache race on Windows. Establish reference
        // bytes sequentially so it cannot prevent measuring the fixed method.
        await _workload.RunSequentialAsync(_baseline);
        var expected = _workload.OutputBytes();
        _workload.Reset(true);
        await _workload.RunAsync(_candidate);
        foreach (var (key, bytes) in _workload.OutputBytes())
        {
            if (!bytes.AsSpan().SequenceEqual(expected[key])) { throw new InvalidOperationException("Image bytes differ."); }
        }
    }
    [IterationSetup] public void Reset() => _workload.Reset(Cold);
    [Benchmark(Baseline = true)] public Task Original() => _workload.RunAsync(_baseline);
    [Benchmark] public Task Deduplicated() => _workload.RunAsync(_candidate);
    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"// peak-working-set-bytes: {process.PeakWorkingSet64}");
        _workload.Dispose();
    }
}
