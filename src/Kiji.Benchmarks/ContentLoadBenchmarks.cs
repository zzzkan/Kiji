using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Markdown;

namespace Kiji.Benchmarks;

/// <summary>Measures Markdown loading against its file I/O cost.</summary>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public class ContentLoadBenchmarks
{
    private const int Files = 1000;

    private string _contentsDirectory = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _contentsDirectory = Path.Combine(Path.GetTempPath(), $"kiji-content-bench-{Guid.NewGuid():N}");
        for (var i = 0; i < Files; i++)
        {
            var directory = Path.Combine(_contentsDirectory, $"post-{i:D5}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "index.md"), $"""
                ---
                title: Synthetic Post {i:D5}
                description: A deterministic post used to measure the content load path.
                createdAt: 2024-01-01T00:00:00Z
                tags:
                  - dotnet
                  - performance
                ---

                # Synthetic Post {i:D5}

                Body text with some *emphasis* and a [link](/). {new string('x', 2000)}
                """);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Directory.Delete(_contentsDirectory, recursive: true);
    }

    /// <summary>
    /// The floor: opening and reading every file, nothing else. If the full load is not
    /// much more than this, no amount of parsing work is worth optimizing and the only
    /// way forward is to stop reading the files.
    /// </summary>
    [Benchmark]
    public long ReadBytesOnly()
    {
        long total = 0;
        var files = new DirectoryInfo(_contentsDirectory).EnumerateFiles("*.md", SearchOption.AllDirectories).ToArray();
        Parallel.For(0, files.Length, index => Interlocked.Add(ref total, File.ReadAllBytes(files[index].FullName).LongLength));
        return total;
    }

    /// <summary>Reading, hashing and front-matter parsing — the parallel part.</summary>
    [Benchmark(Baseline = true)]
    public int ReadAndParse()
    {
        return LoadContents().Count;
    }

    private IReadOnlyList<MarkdownContent<BenchFrontMatter>> LoadContents()
    {
        return new MarkdownContentsBuilder<BenchFrontMatter>(
            _contentsDirectory,
            static (_, _) => Task.FromResult(string.Empty))
            .Build();
    }
}
