namespace Kiji.Sitemaps;

/// <summary>
/// Sitemap support for <see cref="StaticSiteBuilder"/>.
/// </summary>
public static class SitemapStaticSiteBuilderExtensions
{
    /// <summary>Adds a sitemap of generated pages.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="path">The output-relative path, defaulting to <c>sitemap.xml</c>.</param>
    /// <param name="excludedPaths">Site-relative page paths to omit. <c>404.html</c> is always omitted.</param>
    public static StaticSiteBuilder AddSitemap(
        this StaticSiteBuilder builder,
        string path = "sitemap.xml",
        IEnumerable<string>? excludedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddArtifact(path, new SitemapArtifact(excludedPaths).WriteAsync, preserveUnchangedOutput: true);
    }
}
