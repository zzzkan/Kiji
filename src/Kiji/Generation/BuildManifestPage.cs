namespace Kiji.Generation;

/// <summary>
/// A generated page in the build manifest: identity (route + parameters), the exact
/// output it produced, extra files it materialized (e.g. image variants), and the
/// fingerprinted inputs its render consumed.
/// </summary>
internal sealed record BuildManifestPage(
    string OutputRelativePath,
    string RoutePath,
    string ParametersHash,
    string OutputHash,
    IReadOnlyList<BuildManifestDependency> Dependencies,
    IReadOnlyList<string> AdditionalOutputs);
