using Kiji.Incremental;
using Kiji.Rendering;

namespace Kiji.Generation;

internal sealed record RenderedPage(PageRenderRequest Request, string OutputHash, bool Written, byte[] Html,
    OutputStamp? Stamp = null);
