namespace Kiji.SyntheticSite;

/// <summary>
/// How long one stage of a build took. Collected only with <c>--phases</c>: a total
/// that moved is not an explanation until a phase can be pinned on it.
/// </summary>
public sealed record PhaseTiming(string Phase, double ElapsedMs);
