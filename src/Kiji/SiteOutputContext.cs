using System.Diagnostics.CodeAnalysis;

namespace Kiji;

/// <summary>
/// The full-site input handed to <see cref="ISiteArtifact.WriteAsync"/>: site metadata
/// and every generated page.
/// </summary>
public sealed class SiteOutputContext
{
    private readonly Dictionary<string, SitePageInfo> _pagesByIdentity;

    internal SiteOutputContext(SiteInfo site, IReadOnlyList<SitePageInfo> pages)
    {
        Site = site;
        Pages = pages;

        var duplicateIdentity = pages
            .Where(static page => page.ContentIdentity is not null)
            .GroupBy(static page => page.ContentIdentity!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateIdentity is not null)
        {
            var routes = string.Join(
                ", ",
                duplicateIdentity
                    .Select(static page => $"'{page.RoutePath}'")
                    .OrderBy(static route => route, StringComparer.Ordinal));

            throw new InvalidOperationException(
                $"Content identity '{duplicateIdentity.Key}' is associated with multiple generated pages: {routes}. Keyed content used by site artifacts must resolve to exactly one generated page.");
        }

        _pagesByIdentity = pages
            .Where(static page => page.ContentIdentity is not null)
            .ToDictionary(
                static page => page.ContentIdentity!,
                static page => page,
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
    /// (see <see cref="ContentCollection{T}.WithKey"/> and <see cref="KijiApp.MapRoutes{TPage, TContent}"/>).
    /// A keyed content item must be associated with exactly one generated page.
    /// </summary>
    public bool TryResolveRoute(string contentIdentity, [NotNullWhen(true)] out string? routePath)
    {
        if (TryResolvePage(contentIdentity, out var page))
        {
            routePath = page.RoutePath;
            return true;
        }

        routePath = null;
        return false;
    }

    /// <summary>
    /// Resolves the generated page metadata of a content item by its collection key.
    /// A keyed content item must be associated with exactly one generated page.
    /// </summary>
    public bool TryResolvePage(string contentIdentity, [NotNullWhen(true)] out SitePageInfo? page)
    {
        ArgumentNullException.ThrowIfNull(contentIdentity);

        return _pagesByIdentity.TryGetValue(contentIdentity, out page);
    }
}
