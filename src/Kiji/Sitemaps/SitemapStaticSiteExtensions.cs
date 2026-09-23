namespace Kiji.Sitemaps;

/// <summary>
/// Sitemap support for <see cref="StaticSite"/>.
/// </summary>
public static class SitemapStaticSiteExtensions
{
    /// <summary>Registers a sitemap of generated pages.</summary>
    /// <param name="path">The output-relative path, defaulting to <c>sitemap.xml</c>.</param>
    /// <param name="excludedPaths">Site-relative page paths to omit. <c>404.html</c> is always omitted.</param>
    public static StaticSite AddSitemap(
        this StaticSite app,
        string path = "sitemap.xml",
        IEnumerable<string>? excludedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.AddArtifact(path, new SitemapArtifact(excludedPaths).WriteAsync);
    }
}
