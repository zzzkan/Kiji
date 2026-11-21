using Microsoft.Extensions.Hosting;

namespace Kiji.Sitemaps;

/// <summary>Adds sitemaps to a site.</summary>
public static class SitemapHostBuilderExtensions
{
    /// <summary>Adds a sitemap of the generated pages, except <c>404.html</c>.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="path">The output path; <c>sitemap.xml</c> by default.</param>
    /// <param name="excludedPaths">Page paths to leave out, percent-encoded or not.</param>
    public static IHostApplicationBuilder AddSitemap(
        this IHostApplicationBuilder builder,
        string path = "sitemap.xml",
        IEnumerable<string>? excludedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddArtifact(path, new SitemapArtifact(excludedPaths).WriteAsync,
            preserveUnchangedOutput: true, independentOfPages: true);
    }
}
