namespace Kiji.Rendering;

public sealed record PageRenderRequest(
    string SourceIdentifier,
    Type ComponentType,
    IReadOnlyDictionary<string, object?> Parameters,
    string RoutePath,
    string OutputRelativePath,
    string? AssociatedContentIdentity = null,
    bool ExcludeFromSitemap = false);
