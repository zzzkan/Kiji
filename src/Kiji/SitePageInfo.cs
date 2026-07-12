namespace Kiji;

/// <summary>
/// A page in the generated site, as exposed to <see cref="ISiteArtifact"/> implementations.
/// </summary>
/// <param name="RoutePath">The site-relative route, e.g. <c>/blog/my-post/</c>.</param>
/// <param name="OutputRelativePath">The output file path relative to the output directory.</param>
/// <param name="ExcludeFromSitemap">Whether the page opted out of sitemap listing.</param>
/// <param name="ContentIdentity">The associated content key when the page was mapped from a keyed collection.</param>
/// <param name="LastModified">Optional last-modification timestamp, emitted as the sitemap <c>lastmod</c>.</param>
public sealed record SitePageInfo(
    string RoutePath,
    string OutputRelativePath,
    bool ExcludeFromSitemap,
    string? ContentIdentity,
    DateTimeOffset? LastModified = null);
