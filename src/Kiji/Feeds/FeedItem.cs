namespace Kiji.Feeds;

/// <summary>
/// A single feed entry.
/// </summary>
/// <param name="Title">The entry title.</param>
/// <param name="Description">The entry description.</param>
/// <param name="PublishedAt">The publication timestamp.</param>
/// <param name="RoutePath">
/// The entry's site-relative route, e.g. <c>blog/my-post/</c>. Combined with
/// <see cref="SiteInfo.BaseUrl"/> to form the entry's link, so it carries any base path
/// automatically. Kiji does not derive it from the page mapping: the route is the site's
/// to decide, the same way index page links are.
/// </param>
public sealed record FeedItem(
    string Title,
    string Description,
    DateTimeOffset PublishedAt,
    string RoutePath);
