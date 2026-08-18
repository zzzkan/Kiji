namespace Kiji.Rendering;

public sealed record PageRenderRequest(
    string SourceIdentifier,
    Type ComponentType,
    IReadOnlyDictionary<string, object?> Parameters,
    string RoutePath,
    string OutputRelativePath,
    bool ExcludeFromSitemap = false)
{
    /// <summary>
    /// Root component parameters precomputed at snapshot time, so repeated renders
    /// of the same request (build pages, dev server hits) allocate nothing per render.
    /// </summary>
    internal IReadOnlyDictionary<string, object?>? RootParameters { get; init; }
}
