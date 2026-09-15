using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Assets;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class ImageParallelismBenchmarks : IDisposable
{
    private ImageWorkload _workload = null!;
    private ImageProcessor _processor = null!;
    private SemaphoreSlim _gate = null!;
    [Params(640, 2400)] public int Width { get; set; }
    [Params(false, true)] public bool Shared { get; set; }
    [ParamsSource(nameof(OuterValues))] public int Outer { get; set; }
    public IEnumerable<int> OuterValues => new[] { 1, Math.Min(Environment.ProcessorCount, 4), Environment.ProcessorCount }.Distinct();
    [Params(false, true)] public bool InnerSerial { get; set; }
    [GlobalSetup]
    public async Task Setup()
    {
        _workload = new ImageWorkload();
        _gate = new SemaphoreSlim(Math.Min(Environment.ProcessorCount, Outer));
        _processor = new ImageProcessor(null, _gate, InnerSerial ? 1 : Environment.ProcessorCount);
        await _workload.SetupAsync(Width, Shared);
    }
    [IterationSetup] public void Reset() => _workload.Reset(true);
    [Benchmark] public Task Generate() => _workload.RunAsync(_processor);
    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"// peak-working-set-bytes: {process.PeakWorkingSet64}");
        _gate.Dispose();
        _workload.Dispose();
    }
}
