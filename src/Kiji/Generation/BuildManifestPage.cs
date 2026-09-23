namespace Kiji.Generation;

/// <summary>
/// A generated page in the build manifest: identity (route + parameters), the exact
/// output it produced, extra files it materialized (e.g. image variants), and the
/// fingerprinted inputs its render consumed. Cached bytes are verified by hash;
/// Kiji-owned output can also be reused by its recorded stamp.
/// </summary>
internal sealed record BuildManifestPage(
    string OutputRelativePath,
    string RoutePath,
    string? ParametersHash,
    string OutputHash,
    IReadOnlyList<BuildManifestDependency> Dependencies,
    IReadOnlyList<BuildManifestOutput> AdditionalOutputs)
{
    // Slices share one HTML bundle in memory. JSON contains only the slice location.
    [System.Text.Json.Serialization.JsonIgnore]
    public ReadOnlyMemory<byte>? Html { get; set; }
    public int HtmlOffset { get; set; }
    public int HtmlLength { get; set; }
    public OutputStamp? Stamp { get; set; }
    public IReadOnlyList<BuildManifestImage> ImageRequests { get; init; } = [];
    public IReadOnlyList<string> Images { get; init; } = [];
}
