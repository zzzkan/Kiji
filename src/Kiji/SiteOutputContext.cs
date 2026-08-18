namespace Kiji;

/// <summary>
/// The full-site input handed to <see cref="ISiteArtifact.WriteAsync"/>: site metadata
/// and every generated page.
/// </summary>
public sealed class SiteOutputContext
{
    internal SiteOutputContext(SiteInfo site, IReadOnlyList<SitePageInfo> pages, IServiceProvider services)
    {
        Site = site;
        Pages = pages;
        Services = services;
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
    /// The app's services, for artifacts that need content or site configuration.
    /// Artifacts run once every page has been generated, so resolving a
    /// <see cref="ContentDictionary{T}"/> here is safe.
    /// </summary>
    public IServiceProvider Services { get; }
}
