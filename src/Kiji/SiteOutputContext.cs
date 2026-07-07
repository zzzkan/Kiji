using System.Diagnostics.CodeAnalysis;

namespace Kiji;

/// <summary>
/// A page in the generated site, as exposed to <see cref="ISiteArtifact"/> implementations.
/// </summary>
/// <param name="RoutePath">The site-relative route, e.g. <c>/blog/my-post/</c>.</param>
/// <param name="OutputRelativePath">The output file path relative to the output directory.</param>
/// <param name="ExcludeFromSitemap">Whether the page opted out of sitemap listing.</param>
/// <param name="ContentIdentity">The associated content key when the page was mapped from a keyed collection.</param>
public sealed record SitePageInfo(
    string RoutePath,
    string OutputRelativePath,
    bool ExcludeFromSitemap,
    string? ContentIdentity);

/// <summary>
/// The full-site input handed to <see cref="ISiteArtifact.WriteAsync"/>: site metadata
/// and every generated page.
/// </summary>
public sealed class SiteOutputContext
{
    private readonly Dictionary<string, string> _routesByIdentity;

    internal SiteOutputContext(SiteInfo site, IReadOnlyList<SitePageInfo> pages)
    {
        Site = site;
        Pages = pages;
        _routesByIdentity = pages
            .Where(static page => page.ContentIdentity is not null)
            .ToDictionary(
                static page => page.ContentIdentity!,
                static page => page.RoutePath,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The site metadata configured on the builder.
    /// </summary>
    public SiteInfo Site { get; }

    /// <summary>
    /// Every generated page in the site.
    /// </summary>
    public IReadOnlyList<SitePageInfo> Pages { get; }

    /// <summary>
    /// Resolves the route of a content item by its collection key
    /// (see <see cref="ContentCollection{T}.WithKey"/> and <see cref="KijiApp.MapContent{TPage, TContent}"/>).
    /// </summary>
    public bool TryResolveRoute(string contentIdentity, [NotNullWhen(true)] out string? routePath)
    {
        ArgumentNullException.ThrowIfNull(contentIdentity);

        return _routesByIdentity.TryGetValue(contentIdentity, out routePath);
    }
}
