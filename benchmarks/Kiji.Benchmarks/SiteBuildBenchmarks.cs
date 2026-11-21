using System.Globalization;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Kiji.Generation;
using Kiji.SyntheticSite;

namespace Kiji.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
[MemoryDiagnoser]
public class SiteBuildBenchmarks
{
    private readonly Dictionary<string, TimeSpan> _phaseTotals = [];
    private string _root = string.Empty;
    private string _manifestPath = string.Empty;
    private string _editedPost = string.Empty;
    private int _builds;
    private string _originalPost = string.Empty;
    private bool _editToggle;

    [Params(200, 1000)]
    public int Pages { get; set; }

    [Params(BuildScenario.Full, BuildScenario.NoChange, BuildScenario.CacheOnly, BuildScenario.OneEdited, BuildScenario.CodeChanged)]
    public BuildScenario Scenario { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kiji-bdn-{Pages}-{Scenario}-{Guid.NewGuid():N}");
        _editedPost = Path.Combine(_root, "contents", "post-00000", "index.md");
        _manifestPath = Path.Combine(_root, ".kiji", "cache", "manifest.json");

        if (!Directory.Exists(Path.Combine(_root, "contents")))
        {
            await SyntheticSiteWriter.WriteAsync(_root, Pages, includeImages: false);
        }

        var actualPages = Directory.EnumerateFiles(Path.Combine(_root, "contents"), "*.md", SearchOption.AllDirectories).Count();
        if (actualPages != Pages)
        {
            throw new InvalidOperationException(
                $"Benchmark corpus '{_root}' contains {actualPages} posts; expected {Pages}. Use a fresh temporary directory or regenerate this corpus before measuring.");
        }

        await BuildRunner.BuildSiteAsync(_root);
        _originalPost = File.ReadAllText(_editedPost);

        _phaseTotals.Clear();
        _builds = 0;
        BuildPhaseTimer.Observer = (phase, elapsed) =>
            _phaseTotals[phase] = _phaseTotals.GetValueOrDefault(phase) + elapsed;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BuildPhaseTimer.Observer = null;
        Directory.Delete(_root, recursive: true);

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
                var cache = Path.Combine(_root, ".kiji");
                if (Directory.Exists(cache))
                {
                    Directory.Delete(cache, recursive: true);
                }

                break;

            case BuildScenario.OneEdited:
                _editToggle = !_editToggle;
                File.WriteAllText(_editedPost, _originalPost + (_editToggle ? "\n\nEdit A.\n" : "\n\nEdit B.\n"));
                break;

            case BuildScenario.CodeChanged:
                var manifest = JsonNode.Parse(File.ReadAllText(_manifestPath))!;
                manifest["CodeDependencies"]!.AsArray()[0] = "benchmark-code-change";
                File.WriteAllText(_manifestPath, manifest.ToJsonString());
                break;

            case BuildScenario.CacheOnly:
                var output = Path.Combine(_root, "dist");
                if (Directory.Exists(output)) { Directory.Delete(output, recursive: true); }
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
}
