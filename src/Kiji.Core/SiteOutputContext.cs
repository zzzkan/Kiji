namespace Kiji;

/// <summary>What an artifact writer receives: the site and all its pages.</summary>
public sealed class SiteOutputContext
{
    internal SiteOutputContext(SiteInfo site, IReadOnlyList<SitePageInfo> pages, IServiceProvider services)
    {
        Site = site;
        Pages = pages;
        Services = services;
    }

    /// <summary>The site metadata.</summary>
    public SiteInfo Site { get; }

    /// <summary>Every generated page.</summary>
    public IReadOnlyList<SitePageInfo> Pages { get; }

    /// <summary>The site's root services.</summary>
    public IServiceProvider Services { get; }
}
