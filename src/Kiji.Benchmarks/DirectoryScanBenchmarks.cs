using BenchmarkDotNet.Attributes;

namespace Kiji.Benchmarks;

/// <summary>Compares file metadata collection during directory scans.</summary>
/// <remarks>
/// Today every scan enumerates paths and then constructs a <see cref="FileInfo"/> per
/// path, which is a second trip to the filesystem for metadata the directory walk
/// already returned. <see cref="DirectoryInfo.EnumerateFiles(string, SearchOption)"/>
/// hands back entries with that metadata already populated. Adopt it if the end-to-end
/// no-change build improves by at least 10%.
/// </remarks>
[MemoryDiagnoser]
public class DirectoryScanBenchmarks
{
    private const int Files = 1000;

    private string _root = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kiji-scan-bench-{Guid.NewGuid():N}");
        var body = new byte[4096];
        for (var i = 0; i < Files; i++)
        {
            var directory = Path.Combine(_root, $"post-{i:D5}");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "index.md"), body);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>The walk alone, for reference: no metadata is asked for.</summary>
    [Benchmark]
    public int EnumeratePathsOnly()
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            count += path.Length;
        }

        return count;
    }

    [Benchmark(Baseline = true)]
    public long EnumeratePathsThenStat()
    {
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories))
        {
            var info = new FileInfo(Path.GetFullPath(path));
            total += info.Length + info.LastWriteTimeUtc.Ticks;
        }

        return total;
    }

    [Benchmark]
    public long EnumerateFileInfos()
    {
        long total = 0;
        foreach (var info in new DirectoryInfo(_root).EnumerateFiles("*.md", SearchOption.AllDirectories))
        {
            total += info.Length + info.LastWriteTimeUtc.Ticks;
        }

        return total;
    }

    /// <summary>
    /// The walk is single-threaded and, at ~31 µs per file, the largest irreducible
    /// item left in an incremental build. Splitting it by directory lets the
    /// per-directory listings overlap.
    /// </summary>
    [Benchmark]
    public long EnumerateFileInfosParallel()
    {
        var directories = new DirectoryInfo(_root).EnumerateDirectories("*", SearchOption.AllDirectories).ToArray();
        long total = 0;
        Parallel.For(0, directories.Length, index =>
        {
            long subtotal = 0;
            foreach (var info in directories[index].EnumerateFiles("*.md", SearchOption.TopDirectoryOnly))
            {
                subtotal += info.Length + info.LastWriteTimeUtc.Ticks;
            }

            Interlocked.Add(ref total, subtotal);
        });

        foreach (var info in new DirectoryInfo(_root).EnumerateFiles("*.md", SearchOption.TopDirectoryOnly))
        {
            total += info.Length + info.LastWriteTimeUtc.Ticks;
        }

        return total;
    }

    /// <summary>
    /// The shape reconciliation needs: every file's path relative to the root. Today
    /// that is <see cref="Path.GetRelativePath"/> per entry, which re-walks and
    /// reallocates a string already implied by the enumeration.
    /// </summary>
    [Benchmark]
    public int EnumerateRelativePathsViaGetRelativePath()
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            count += Path.GetRelativePath(_root, path).Length;
        }

        return count;
    }

    [Benchmark]
    public int EnumerateRelativePathsBySlicing()
    {
        var prefix = _root.Length + 1;
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            count += path.AsSpan(prefix).Length;
        }

        return count;
    }
}
