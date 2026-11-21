namespace Kiji.SyntheticSite.Harness;

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
