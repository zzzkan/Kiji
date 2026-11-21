namespace Kiji.Rendering;

internal sealed record PageRenderRequest(
    string SourceIdentifier,
    Type ComponentType,
    IReadOnlyDictionary<string, object?> Parameters,
    string RoutePath,
    string OutputRelativePath)
{
    internal IReadOnlyDictionary<string, object?>? RootParameters { get; init; }
}
