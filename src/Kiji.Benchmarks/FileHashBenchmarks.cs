using BenchmarkDotNet.Attributes;
using Kiji.Generation;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class FileHashBenchmarks
{
    private string _path = "";
    [Params(2048, 131072, 8388608)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"kiji-hash-{Guid.NewGuid():N}");
        var bytes = new byte[Bytes];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(_path, bytes);
        if (ReadAll() != Streaming()) { throw new InvalidOperationException("Hash mismatch."); }
    }
    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);
    [Benchmark(Baseline = true)]
    public string ReadAll() => BuildFingerprint.HashBytes(File.ReadAllBytes(_path));
    [Benchmark]
    public string Streaming() => BuildFingerprint.HashFile(_path);
}
