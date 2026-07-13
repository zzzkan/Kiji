using System.Text;
using BenchmarkDotNet.Attributes;

namespace Kiji.Benchmarks;

/// <summary>
/// Measures the page write path: the current StreamWriter-over-async-FileStream shape
/// versus a single pre-encoded write (Phase 1-2 / 2-2 baseline and target).
/// </summary>
[MemoryDiagnoser]
public class PageWriteBenchmarks
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private string _html = string.Empty;
    private byte[] _htmlUtf8 = [];
    private string _directory = string.Empty;
    private string _path = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _html = string.Join(
            "\n",
            Enumerable.Range(0, 500).Select(static i =>
                $"<p>Line {i} of a generated page, long enough to reach a typical page size.</p>"));
        _htmlUtf8 = Utf8NoBom.GetBytes(_html);
        _directory = Path.Combine(Path.GetTempPath(), $"kiji-write-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "index.html");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Benchmark(Baseline = true)]
    public async Task CurrentStreamWriterAsync()
    {
        var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        var writer = new StreamWriter(stream, Encoding.UTF8);
        await using (stream)
        await using (writer)
        {
            await writer.WriteAsync(_html);
        }
    }

    [Benchmark]
    public void SinglePreEncodedWrite()
    {
        using var handle = File.OpenHandle(
            _path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileOptions.None,
            preallocationSize: _htmlUtf8.Length);
        RandomAccess.Write(handle, _htmlUtf8, fileOffset: 0);
    }
}
