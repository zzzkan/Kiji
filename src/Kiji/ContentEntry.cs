namespace Kiji;

/// <summary>A stable content identity, its value, and a digest of all data affecting that value.</summary>
/// <param name="Id">Stable source-local identifier, independent of checkout location.</param>
/// <param name="Value">The immutable value for this build snapshot.</param>
/// <param name="Digest">Changes whenever the value changes; null disables reuse of readers.</param>
public sealed record ContentEntry<T>(string Id, T Value, string? Digest) where T : class;
