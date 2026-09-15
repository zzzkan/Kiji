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

    /// <summary>The site metadata supplied to this generation.</summary>
    public SiteInfo Site { get; }

    /// <summary>
    /// Every generated page in the site.
    /// </summary>
    public IReadOnlyList<SitePageInfo> Pages { get; }

    /// <summary>The site services available during artifact generation.</summary>
    public IServiceProvider Services { get; }
}
