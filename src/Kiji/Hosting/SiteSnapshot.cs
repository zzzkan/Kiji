using System.Collections.Frozen;
using Kiji.Rendering;
using Microsoft.AspNetCore.Http;

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
        // Ordinal (case-sensitive) so dev matches production static hosts, which
        // serve files case-sensitively.
        PagesByRoute = pages.ToFrozenDictionary(
            static page => PathString.FromUriComponent(page.RoutePath).Value!,
            StringComparer.Ordinal);
    }

    internal IReadOnlyList<PageRenderRequest> Pages { get; }

    internal FrozenDictionary<string, PageRenderRequest> PagesByRoute { get; }
}
