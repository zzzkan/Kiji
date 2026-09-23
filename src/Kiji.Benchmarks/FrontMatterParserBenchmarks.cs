using BenchmarkDotNet.Attributes;
using Kiji.Markdown;

namespace Kiji.Benchmarks;

/// <summary>Measures front matter and body parsing.</summary>
[MemoryDiagnoser]
public class FrontMatterParserBenchmarks
{
    private string _content = string.Empty;
    private readonly YamlDotNet.Serialization.IDeserializer _deserializer = MarkdownFrontMatterParser.CreateDeserializer(null);

    [GlobalSetup]
    public void Setup()
    {
        _content = $"""
            ---
            title: Synthetic Post 00042
            description: Deterministic benchmark post exercising the markdown pipeline.
            createdAt: 2024-06-15
            tags: [dotnet, performance]
            ---

            # Synthetic Post 00042

            {string.Join("\n\n", Enumerable.Range(0, 40).Select(static i => $"Paragraph {i} with enough text to make the body realistic for a blog article body."))}
            """;
    }

    [Benchmark]
    public (BenchFrontMatter FrontMatter, string Body) ParseContentAndBody()
    {
        return MarkdownFrontMatterParser.ParseContentAndBody<BenchFrontMatter>(_content, _deserializer);
    }
}
