using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Generation;

namespace Kiji.Benchmarks;

/// <summary>
/// Decides whether a page write should be preceded by a check that the target already
/// holds those bytes. Re-rendering a page does not mean its output changed — a
/// dependency edit that never reaches the markup, or <c>--force</c> on unchanged
/// content, produce byte-identical documents — and a file create is far more expensive
/// than a read.
/// </summary>
/// <remarks>
/// Adopt the check only if skipping an identical write is at least 50% cheaper than
/// performing it, and if the worst case (same length, different bytes, so the whole
/// file is read and hashed before writing anyway) stays acceptable.
/// </remarks>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public class OutputWriteSkipBenchmarks
{
    private const int Files = 1000;

    private readonly string[] _paths = new string[Files];
    private readonly (long Length, DateTime LastWriteTimeUtc, string Hash)[] _recorded =
        new (long, DateTime, string)[Files];

    private byte[] _rendered = [];
    private byte[] _onDisk = [];
    private string _directory = string.Empty;
    private string _onDiskHash = string.Empty;

    /// <summary>
    /// Whether the file already on disk is what this build would write. False is the
    /// worst case for the check: identical length, so it costs a full read and hash
    /// and then writes anyway.
    /// </summary>
    [Params(true, false)]
    public bool OnDiskMatches { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // A representative generated page: ~40 KB of markup.
        _rendered = System.Text.Encoding.UTF8.GetBytes(string.Join(
            "\n",
            Enumerable.Range(0, 500).Select(static i =>
                $"<p>Line {i} of a generated page, long enough to reach a typical page size.</p>")));

        _onDisk = (byte[])_rendered.Clone();
        if (!OnDiskMatches)
        {
            _onDisk[^1] ^= 0x01;
        }

        _onDiskHash = BuildFingerprint.HashBytes(_onDisk);

        _directory = Path.Combine(Path.GetTempPath(), $"kiji-write-skip-{Guid.NewGuid():N}");
        for (var i = 0; i < Files; i++)
        {
            var pageDirectory = Path.Combine(_directory, $"page-{i:D5}");
            Directory.CreateDirectory(pageDirectory);
            _paths[i] = Path.Combine(pageDirectory, "index.html");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Every strategy starts from the same bytes on disk, and from a manifest that
        // describes them accurately — which is what the stamp gate gets to trust.
        for (var i = 0; i < Files; i++)
        {
            File.WriteAllBytes(_paths[i], _onDisk);
            var info = new FileInfo(_paths[i]);
            _recorded[i] = (info.Length, info.LastWriteTimeUtc, _onDiskHash);
        }
    }

    [Benchmark(Baseline = true)]
    public void Write()
    {
        for (var i = 0; i < Files; i++)
        {
            WriteFile(_paths[i], _rendered);
        }
    }

    /// <summary>
    /// The stamp gate: one stat per page, and the previous build's recorded hash is
    /// trusted while the stamp holds — the same short-circuit the skip checks already
    /// use. No file is read.
    /// </summary>
    [Benchmark]
    public void CompareStampThenWrite()
    {
        var hash = BuildFingerprint.HashBytes(_rendered);
        for (var i = 0; i < Files; i++)
        {
            if (!StampSaysCurrent(_paths[i], _recorded[i], hash))
            {
                WriteFile(_paths[i], _rendered);
            }
        }
    }

    [Benchmark]
    public void CompareThenWrite()
    {
        var hash = BuildFingerprint.HashBytes(_rendered);
        for (var i = 0; i < Files; i++)
        {
            if (!AlreadyHolds(_paths[i], _rendered.Length, hash))
            {
                WriteFile(_paths[i], _rendered);
            }
        }
    }

    private static bool StampSaysCurrent(
        string path,
        (long Length, DateTime LastWriteTimeUtc, string Hash) recorded,
        string hash)
    {
        if (!string.Equals(recorded.Hash, hash, StringComparison.Ordinal))
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Exists
            && info.Length == recorded.Length
            && info.LastWriteTimeUtc == recorded.LastWriteTimeUtc;
    }

    private static bool AlreadyHolds(string path, int length, string hash)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != length)
        {
            return false;
        }

        return string.Equals(BuildFingerprint.HashFile(path), hash, StringComparison.Ordinal);
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        using var handle = File.OpenHandle(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileOptions.None,
            preallocationSize: bytes.Length);
        RandomAccess.Write(handle, bytes, fileOffset: 0);
    }
}
