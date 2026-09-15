namespace Kiji.Feeds;

/// <summary>An RSS feed entry.</summary>
/// <param name="Title">The entry title.</param>
/// <param name="Description">The entry description.</param>
/// <param name="PublishedAt">The publication timestamp.</param>
/// <param name="RoutePath">The site-relative route combined with <see cref="SiteInfo.BaseUrl"/> to form the entry URL.</param>
public sealed record FeedItem(
    string Title,
    string Description,
    DateTimeOffset PublishedAt,
    string RoutePath);
