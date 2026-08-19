namespace Kiji.SyntheticSite;

/// <summary>
/// Measurements for a single end-to-end build run.
/// </summary>
public sealed record RunResult(
    int Run,
    double ElapsedMs,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long PeakWorkingSetBytes,
    IReadOnlyList<PhaseTiming>? Phases = null);
