namespace Kiji.Paths;

internal sealed record ResolvedSitePaths
{
    internal string? AssetManifestPath { get; init; }
    internal string? AssetEndpointsPath { get; init; }

    public required string OutputDirectory { get; init; }

    public string? ImageCacheDirectory { get; init; }
}
