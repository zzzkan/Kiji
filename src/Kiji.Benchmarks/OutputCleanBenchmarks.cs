using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Kiji.Benchmarks;

/// <summary>
/// Decides how a build should guarantee that the output directory holds exactly what
/// it produced. Today it deletes the tree and recreates every page directory; the
/// alternative keeps the tree and deletes only what the new manifest does not claim.
/// </summary>
/// <remarks>
/// Both strategies must leave the same directory set behind, so both include creating
/// the page directories — real creates for the delete-first strategy, existence checks
/// for the reconciling one. Writing the pages themselves is identical either way and
/// is left out.
/// </remarks>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public class OutputCleanBenchmarks
{
    private const int Pages = 1000;

    private readonly string[] _pageDirectories = new string[Pages];
    private HashSet<string> _expected = [];
    private string _root = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kiji-clean-bench-{Guid.NewGuid():N}");
        for (var i = 0; i < Pages; i++)
        {
            _pageDirectories[i] = Path.Combine(_root, $"page-{i:D5}");
        }

        _expected = [.. Enumerable.Range(0, Pages).Select(static i => Path.Combine($"page-{i:D5}", "index.html"))];
        _expected = new HashSet<string>(_expected, StringComparer.OrdinalIgnoreCase);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // A populated output directory, as either strategy would find it.
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        var page = new byte[4096];
        for (var i = 0; i < Pages; i++)
        {
            Directory.CreateDirectory(_pageDirectories[i]);
            File.WriteAllBytes(Path.Combine(_pageDirectories[i], "index.html"), page);
        }
    }

    [Benchmark(Baseline = true)]
    public void DeleteTreeThenRecreate()
    {
        Directory.Delete(_root, recursive: true);
        Directory.CreateDirectory(_root);
        foreach (var directory in _pageDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    [Benchmark]
    public void ReconcileInPlace()
    {
        Directory.CreateDirectory(_root);
        foreach (var directory in _pageDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            if (!_expected.Contains(Path.GetRelativePath(_root, file)))
            {
                File.Delete(file);
            }
        }
    }
}
