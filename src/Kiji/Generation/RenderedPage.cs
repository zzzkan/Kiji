using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// A page rendered by the current build and the XxHash128 of the bytes it wrote.
/// </summary>
internal sealed record RenderedPage(PageRenderRequest Request, string OutputHash);
