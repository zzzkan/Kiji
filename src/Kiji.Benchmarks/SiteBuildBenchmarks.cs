using System.Globalization;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Generation;
using Kiji.SyntheticSite;

namespace Kiji.Benchmarks;

/// <summary>
/// Measures a whole site build over the frozen synthetic workload. This is the
/// authoritative end-to-end number: BenchmarkDotNet's warmup absorbs the JIT cost that
/// makes a hand-rolled harness's first run 500 ms slower than its steady state, and it
/// runs every case in one process under identical conditions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunStrategy.Monitoring"/> with one invocation per iteration is the mode
/// for expensive operations with side effects — a build writes to disk, so it cannot be
/// invoked in a tight loop. Warmup is set explicitly rather than left to the strategy's
/// default, because absorbing the JIT is the whole reason this exists.
/// </para>
/// <para>
/// The corpus is generated once per page count and reused across runs, matching the
/// <c>--root</c> convention of <c>Kiji.SyntheticSite</c>: regenerating it would put
/// content generation inside the comparison. It is left on disk deliberately.
/// </para>
/// </remarks>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public partial class SiteBuildBenchmarks
{
    private readonly Dictionary<string, TimeSpan> _phaseTotals = [];
    private string _root = string.Empty;
    private string _manifestPath = string.Empty;
    private string _editedPost = string.Empty;
    private int _builds;

    [Params(200, 1000)]
    public int Pages { get; set; }

    [Params(BuildScenario.Full, BuildScenario.NoChange, BuildScenario.OneEdited, BuildScenario.CodeChanged)]
    public BuildScenario Scenario { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kiji-bdn-corpus-{Pages}");
        _editedPost = Path.Combine(_root, "contents", "post-00000", "index.md");
        _manifestPath = Path.Combine(_root, ".kiji", "cache", "build-manifest.json");

        if (!Directory.Exists(Path.Combine(_root, "contents")))
        {
            await SyntheticSiteWriter.WriteAsync(_root, Pages, includeImages: false);
        }

        // Every scenario starts from a site that has already been built once: the
        // output directory populated and a manifest on disk.
        await BuildRunner.BuildSiteAsync(_root);

        // Phase attribution under the same conditions as the headline number. Warmup
        // and measured iterations are pooled, so read these as proportions, not as
        // per-iteration times.
        _phaseTotals.Clear();
        _builds = 0;
        BuildPhaseTimer.Observer = (phase, elapsed) =>
            _phaseTotals[phase] = _phaseTotals.GetValueOrDefault(phase) + elapsed;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BuildPhaseTimer.Observer = null;

        if (_builds == 0)
        {
            return;
        }

        var breakdown = string.Join(
            " ",
            _phaseTotals.Select(phase => string.Create(
                CultureInfo.InvariantCulture,
                $"{phase.Key}={phase.Value.TotalMilliseconds / _builds:F0}")));
        Console.WriteLine($"// phases per build ({Pages}/{Scenario}, {_builds} builds): {breakdown}");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        switch (Scenario)
        {
            case BuildScenario.Full:
                // Removing the manifest is what forces every page to re-render; the
                // output directory stays populated, as it would on a CI rebuild.
                var cache = Path.Combine(_root, ".kiji");
                if (Directory.Exists(cache))
                {
                    Directory.Delete(cache, recursive: true);
                }

                break;

            case BuildScenario.OneEdited:
                File.AppendAllText(_editedPost, $"\n\nEdited at {DateTimeOffset.UtcNow:O}.\n");
                break;

            case BuildScenario.CodeChanged:
                // An assembly's module version ID is how the planner sees a code change,
                // and it cannot be changed from inside the running process. Rewriting the
                // one the manifest recorded produces exactly the same decision: every page
                // re-renders, with the manifest still intact and still trusted.
                var manifest = File.ReadAllText(_manifestPath);
                File.WriteAllText(_manifestPath, MvidPattern().Replace(
                    manifest,
                    $":{Guid.NewGuid():N}\"",
                    count: 1));
                break;

            case BuildScenario.NoChange:
            default:
                break;
        }
    }

    [Benchmark]
    public Task BuildSiteAsync()
    {
        _builds++;
        return BuildRunner.BuildSiteAsync(_root);
    }

    /// <summary>Matches the hex module version ID in a manifest's assembly entry.</summary>
    [GeneratedRegex(":[0-9a-f]{32}\"")]
    private static partial Regex MvidPattern();
}
