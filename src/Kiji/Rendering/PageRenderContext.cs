namespace Kiji.Rendering;

/// <summary>
/// Ambient context of the page render currently in progress. Set by <see cref="KijiApp"/>
/// around each page render and read by content renderers that emit page-relative output
/// (e.g. markdown image materialization into the page's output directory).
/// </summary>
public sealed class PageRenderContext
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
    /// The directory of the page's output file relative to the output root;
    /// empty for root pages.
    /// </summary>
    public required string OutputRelativeDirectory { get; init; }

    /// <summary>
    /// Dependency recorder for the incremental build, or <see langword="null"/> when
    /// the render is not being tracked (dev server, direct renders).
    /// </summary>
    internal Generation.BuildDependencyRecorder? Dependencies { get; init; }

    internal static void SetCurrent(PageRenderContext? context)
    {
        Ambient.Value = context;
    }
}
