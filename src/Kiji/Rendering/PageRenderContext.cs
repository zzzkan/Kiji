namespace Kiji.Rendering;

internal sealed class PageRenderContext
{
    private static readonly AsyncLocal<PageRenderContext?> Ambient = new();

    public static PageRenderContext? Current => Ambient.Value;

    public required string RoutePath { get; init; }

    internal Generation.BuildDependencyRecorder? Dependencies { get; init; }

    internal CancellationToken CancellationToken { get; init; }

    internal SharedRenderLifetime? SharedRenders { get; init; }

    internal Kiji.Assets.AssetResources Assets { get; init; } = Kiji.Assets.AssetResources.Empty;

    internal static void SetCurrent(PageRenderContext? context)
    {
        Ambient.Value = context;
    }
}
