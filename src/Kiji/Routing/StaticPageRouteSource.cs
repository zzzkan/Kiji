namespace Kiji.Routing;

/// <summary>
/// A single expansion of a dynamic route template.
/// </summary>
/// <param name="RouteValues">Values bound to the template's route parameters.</param>
/// <param name="AssociatedContentIdentity">Optional identity linking the page to a content item (used by feeds).</param>
/// <param name="ExcludeFromSitemap">Whether the page is omitted from the sitemap.</param>
/// <param name="LastModified">Optional last-modification timestamp, emitted as the sitemap <c>lastmod</c>.</param>
public sealed record StaticPageRouteEntry(
    IReadOnlyDictionary<string, string> RouteValues,
    string? AssociatedContentIdentity = null,
    bool ExcludeFromSitemap = false,
    DateTimeOffset? LastModified = null);
