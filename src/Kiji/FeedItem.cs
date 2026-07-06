namespace Kiji;

/// <summary>
/// Feed metadata for a single content item. The item's route is resolved
/// automatically from the content-to-page mapping.
/// </summary>
/// <param name="Title">The entry title.</param>
/// <param name="Description">The entry description.</param>
/// <param name="PublishedAt">The publication timestamp.</param>
public sealed record FeedItem(string Title, string Description, DateTimeOffset PublishedAt);
