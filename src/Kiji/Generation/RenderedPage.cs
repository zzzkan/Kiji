using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// A page rendered by the current build and the SHA-256 of the bytes it produced.
/// </summary>
/// <param name="Written">
/// Whether those bytes were written. A re-rendered page whose output turns out to be
/// what is already on disk is left alone.
/// </param>
/// <param name="Stamp">The written file's stamp, read only in an output directory this build created.</param>
internal sealed record RenderedPage(PageRenderRequest Request, string OutputHash, bool Written, byte[] Html,
    OutputStamp? Stamp = null);
