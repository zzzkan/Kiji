namespace Kiji.Sitemaps;

/// <summary>
/// Sitemap support for <see cref="KijiApp"/>.
/// </summary>
public static class SitemapKijiAppExtensions
{
    /// <summary>
    /// Generates a sitemap from every generated page, excluding pages marked with
    /// <c>ExcludeFromSitemap</c> (e.g. the not-found page).
    /// </summary>
    /// <param name="app">The Kiji application.</param>
    /// <param name="path">The output path relative to the output directory.</param>
    public static KijiApp MapSitemap(this KijiApp app, string path = "sitemap.xml")
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.MapArtifact(new SitemapArtifact(path));
    }
}
