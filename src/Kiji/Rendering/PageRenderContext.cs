namespace Kiji.Rendering;

/// <summary>
/// Ambient context of the page render currently in progress. Set by <see cref="StaticSite"/>
/// around each page render to track dependencies and coordinate shared content work.
/// </summary>
internal sealed class PageRenderContext
{
    private static readonly AsyncLocal<PageRenderContext?> Ambient = new();

    /// <summary>
    /// The context of the page render in progress on the current async flow,
    /// or <see langword="null"/> outside a page render.
    /// </summary>
    public static PageRenderContext? Current => Ambient.Value;

    /// <summary>
    /// The site-relative route of the page being rendered, e.g. <c>/blog/my-post/</c>.
    /// </summary>
    public required string RoutePath { get; init; }

    /// <summary>
    /// Dependency recorder for the incremental build, or <see langword="null"/> when
    /// the render is not being tracked (dev server, direct renders).
    /// </summary>
    internal Generation.BuildDependencyRecorder? Dependencies { get; init; }

    internal CancellationToken CancellationToken { get; init; }

    internal SharedRenderLifetime? SharedRenders { get; init; }

    internal Kiji.Assets.AssetResources Assets { get; init; } = Kiji.Assets.AssetResources.Empty;

    internal static void SetCurrent(PageRenderContext? context)
    {
        Ambient.Value = context;
    }
}
