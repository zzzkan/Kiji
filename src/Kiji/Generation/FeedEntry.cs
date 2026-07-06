namespace Kiji.Generation;

public sealed record FeedEntry(
    string Identity,
    string Title,
    string Description,
    DateTimeOffset PublishedAt,
    string RoutePath);