using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// A page rendered by the current build and the XxHash128 of the bytes it produced.
/// </summary>
/// <param name="Written">
/// Whether those bytes were written. A re-rendered page whose output turns out to be
/// what is already on disk is left alone.
/// </param>
internal sealed record RenderedPage(PageRenderRequest Request, string OutputHash, bool Written);
