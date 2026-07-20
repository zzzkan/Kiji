namespace Kiji.Generation;

/// <summary>
/// A generated page in the build manifest: identity (route + parameters), the exact
/// output it produced, extra files it materialized (e.g. image variants), and the
/// fingerprinted inputs its render consumed. The output stamp (length + last write
/// time) lets later builds trust <see cref="OutputHash"/> without re-reading the
/// output file when it is untouched.
/// </summary>
internal sealed record BuildManifestPage(
    string OutputRelativePath,
    string RoutePath,
    string ParametersHash,
    string OutputHash,
    IReadOnlyList<BuildManifestDependency> Dependencies,
    IReadOnlyList<string> AdditionalOutputs,
    long? OutputLength = null,
    DateTime? OutputLastWriteTimeUtc = null);
