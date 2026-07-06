using System.Collections.Frozen;
using Kiji.Rendering;

namespace Kiji.Hosting;

/// <summary>
/// An immutable plan of every page in the site at a point in time.
/// The dev server swaps snapshots when content changes; a build uses exactly one.
/// </summary>
internal sealed class SiteSnapshot
{
    internal SiteSnapshot(IReadOnlyList<PageRenderRequest> pages)
    {
        Pages = pages;
        PagesByRoute = pages.ToFrozenDictionary(
            static page => page.RoutePath,
            StringComparer.OrdinalIgnoreCase);
    }

    internal IReadOnlyList<PageRenderRequest> Pages { get; }

    internal FrozenDictionary<string, PageRenderRequest> PagesByRoute { get; }
}
