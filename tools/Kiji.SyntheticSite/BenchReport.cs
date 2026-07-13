namespace Kiji.SyntheticSite;

/// <summary>
/// The full harness report serialized to JSON for before/after comparisons.
/// </summary>
public sealed record BenchReport(
    DateTimeOffset Timestamp,
    int Pages,
    bool Images,
    string RuntimeVersion,
    int ProcessorCount,
    bool ServerGc,
    IReadOnlyList<RunResult> Runs,
    double MedianElapsedMs,
    RunResult? MutationRun = null);
