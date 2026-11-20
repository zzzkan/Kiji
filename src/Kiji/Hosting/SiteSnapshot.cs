using Kiji.Rendering;
using Microsoft.AspNetCore.Http;

namespace Kiji.Hosting;

internal sealed class SiteSnapshot
{
    internal SiteSnapshot(IReadOnlyList<PageRenderRequest> pages)
    {
        Pages = pages;
    }

    internal IReadOnlyList<PageRenderRequest> Pages { get; }

    internal Dictionary<string, PageRenderRequest> PagesByRoute => LazyInitializer.EnsureInitialized(ref field,
        () => Pages.ToDictionary(
            static page => PathString.FromUriComponent(page.RoutePath).Value!,
            StringComparer.Ordinal));
}
