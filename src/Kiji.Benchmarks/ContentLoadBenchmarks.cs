using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Markdown;

namespace Kiji.Benchmarks;

/// <summary>Separately measures reading/parsing sources and constructing the content dictionary.</summary>
/// <remarks>
/// Reading and parsing the files is parallel; turning them into a keyed dictionary is
/// not. This measures both so the next optimization goes where the time actually is
/// rather than where it looks like it should be.
/// </remarks>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public class ContentLoadBenchmarks
{
    private const int Files = 1000;

    private string _contentsDirectory = string.Empty;
    private IReadOnlyList<MarkdownContent<BenchFrontMatter>> _contents = [];

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
        _contents = LoadContents();
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

    /// <summary>Walking the tree for stamps only — what a cache hit would cost.</summary>
    [Benchmark]
    public long StampsOnly()
    {
        long total = 0;
        foreach (var file in new DirectoryInfo(_contentsDirectory).EnumerateFiles("*.md", SearchOption.AllDirectories))
        {
            total += file.Length + file.LastWriteTimeUtc.Ticks;
        }

        return total;
    }

    /// <summary>Reading, hashing and front-matter parsing — the parallel part.</summary>
    [Benchmark(Baseline = true)]
    public int ReadAndParse()
    {
        return LoadContents().Count;
    }

    /// <summary>
    /// The same, plus what turns the result into a keyed dictionary: key selection, a
    /// duplicate check, two <see cref="FrozenDictionary{TKey, TValue}"/> builds and an
    /// ordered entry array. All of that runs on one thread.
    /// </summary>
    [Benchmark]
    public int ReadAndParseThenIndex()
    {
        return Index(LoadContents());
    }

    [Benchmark]
    public int IndexOnly() => Index(_contents);

    private static int Index(IReadOnlyList<MarkdownContent<BenchFrontMatter>> contents)
    {

        var index = new Dictionary<string, MarkdownContent<BenchFrontMatter>>(contents.Count, StringComparer.OrdinalIgnoreCase);
        var provenance = new Dictionary<string, string?>(contents.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var content in contents)
        {
            var key = content.FileInfo.Slug;
            index[key] = content;
            provenance[key] = content.FileInfo.FilePath;
        }

        var entries = index.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ToArray();
        var frozen = index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var frozenProvenance = provenance.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        return entries.Length + frozen.Count + frozenProvenance.Count;
    }

    private IReadOnlyList<MarkdownContent<BenchFrontMatter>> LoadContents()
    {
        return new MarkdownContentsBuilder<BenchFrontMatter>(
            _contentsDirectory,
            static (_, _) => Task.FromResult(string.Empty))
            .Build();
    }
}
