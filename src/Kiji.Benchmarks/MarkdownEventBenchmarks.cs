using MarkdownParsedSource = Kiji.Benchmarks.MarkdownCacheCandidate.MarkdownParsedSource;
using MarkdownDiskCache = Kiji.Benchmarks.MarkdownCacheCandidate.MarkdownDiskCache;
using YamlEventParser = Kiji.Benchmarks.MarkdownCacheCandidate.YamlEventParser;
using YamlEventCodec = Kiji.Benchmarks.MarkdownCacheCandidate.YamlEventCodec;
using BenchmarkDotNet.Attributes;
using Kiji.Markdown;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Kiji.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class MarkdownEventBenchmarks
{
    private const string Yaml = "title: Example\ndescription: Description\ncreatedAt: 2024-01-01\ntags: [dotnet, performance]\n";
    private readonly IDeserializer _deserializer = MarkdownFrontMatterParser.CreateDeserializer(null);
    private byte[] _events = [];
    private string _path = "";
    private Dictionary<string, MarkdownParsedSource> _sources = [];

    [GlobalSetup]
    public void Setup()
    {
        _events = YamlEventCodec.Parse(Yaml);
        _path = Path.Combine(Path.GetTempPath(), $"kiji-events-{Guid.NewGuid():N}", "snapshot.bin");
        _sources = new Dictionary<string, MarkdownParsedSource>
        {
            ["post/index.md"] = new("body", _events, "hash", new MarkdownFileStamp(100, DateTime.UnixEpoch)),
        };
        MarkdownDiskCache.Save(_path, _sources);
        if (ParseAndConvert().Title != ReplayAndConvert().Title) { throw new InvalidOperationException("YAML mismatch."); }
    }
    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(Path.GetDirectoryName(_path)!, true);
    [Benchmark(Baseline = true)]
    public BenchFrontMatter ParseAndConvert() => _deserializer.Deserialize<BenchFrontMatter>(Yaml);
    [Benchmark]
    public ParsingEvent[] ParseEvents() => YamlEventCodec.ParseEvents(Yaml);
    [Benchmark]
    public byte[] ParseAndEncodeEvents() => YamlEventCodec.Parse(Yaml);
    [Benchmark]
    public BenchFrontMatter ReplayAndConvert() => _deserializer.Deserialize<BenchFrontMatter>(new YamlEventParser(YamlEventCodec.Decode(_events)));
    [Benchmark]
    public int ReadSnapshot() => MarkdownDiskCache.Load(_path).Count;
    [Benchmark]
    public void WriteSnapshot() => MarkdownDiskCache.Save(_path, _sources);
}
