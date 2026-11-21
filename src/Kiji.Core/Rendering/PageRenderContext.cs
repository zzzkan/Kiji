using Kiji.Incremental;
namespace Kiji.Rendering;

internal sealed class PageRenderContext
{
    private static readonly AsyncLocal<PageRenderContext?> Ambient = new();

    public static PageRenderContext? Current => Ambient.Value;

    public required string RoutePath { get; init; }

    internal BuildDependencyRecorder? Dependencies { get; init; }

    internal CancellationToken CancellationToken { get; init; }

    internal SharedRenderLifetime? SharedRenders { get; init; }

    internal AssetResources Assets { get; init; } = AssetResources.Empty;

    internal static void SetCurrent(PageRenderContext? context)
    {
        Ambient.Value = context;
    }
}
