namespace Kiji.Sitemaps;

/// <summary>
/// Sitemap support for <see cref="StaticSite"/>.
/// </summary>
public static class SitemapStaticSiteExtensions
{
    /// <summary>Registers a sitemap of generated pages, excluding the not-found page.</summary>
    /// <param name="path">The output-relative path, defaulting to <c>sitemap.xml</c>.</param>
    public static StaticSite AddSitemap(this StaticSite app, string path = "sitemap.xml")
    {
        ArgumentNullException.ThrowIfNull(app);

        var artifact = new SitemapArtifact(path);
        return app.AddArtifact(artifact.OutputRelativePath, SitemapArtifact.WriteAsync);
    }
}
