using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Assets;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class ImageProcessingBenchmarks : IDisposable
{
    private ImageWorkload _workload = null!;
    private readonly ImageProcessor _processor = new();
    [Params(640, 2400)] public int Width { get; set; }
    [Params(false, true)] public bool Shared { get; set; }
    [Params(false, true)] public bool Cold { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _workload = new ImageWorkload();
        await _workload.SetupAsync(Width, Shared);
        await _workload.RunAsync(_processor);
    }
    [IterationSetup] public void Reset() => _workload.Reset(Cold);
    [Benchmark] public Task ProcessImages() => _workload.RunAsync(_processor);
    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"// peak-working-set-bytes: {process.PeakWorkingSet64}");
        _workload.Dispose();
    }
}
