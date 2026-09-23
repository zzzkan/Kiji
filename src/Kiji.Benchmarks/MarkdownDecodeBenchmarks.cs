using System.Text;
using BenchmarkDotNet.Attributes;
using Kiji.Markdown;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class MarkdownDecodeBenchmarks
{
    private byte[] _bytes = [];

    [Params(1024, 16384)]
    public int Characters { get; set; }

    [Params(false, true)]
    public bool Bom { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var encoding = new UTF8Encoding(Bom);
        _bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("# 日本語 🐦\n\nBody".PadRight(Characters, 'x'))];
        if (StreamReaderDecode() != DirectDecode()) { throw new InvalidOperationException("Decoded content differs."); }
    }

    [Benchmark(Baseline = true)]
    public string StreamReaderDecode()
    {
        using var reader = new StreamReader(new MemoryStream(_bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    [Benchmark]
    public string DirectDecode() => MarkdownSourceReader.DecodeText(_bytes);
}
