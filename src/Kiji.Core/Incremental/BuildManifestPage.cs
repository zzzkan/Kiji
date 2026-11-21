namespace Kiji.Incremental;

internal sealed record BuildManifestPage(
    string OutputRelativePath,
    string RoutePath,
    string? ParametersHash,
    string OutputHash,
    IReadOnlyList<BuildManifestDependency> Dependencies,
    IReadOnlyList<BuildManifestOutput> AdditionalOutputs)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ReadOnlyMemory<byte>? Html { get; set; }
    public int HtmlOffset { get; set; }
    public int HtmlLength { get; set; }
    public OutputStamp? Stamp { get; set; }
    public IReadOnlyList<BuildManifestImage> ImageRequests { get; init; } = [];
    public IReadOnlyList<string> Images { get; init; } = [];
}
