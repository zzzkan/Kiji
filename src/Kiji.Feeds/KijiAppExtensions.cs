namespace Kiji.Feeds;

/// <summary>
/// RSS feed support for <see cref="KijiApp"/>.
/// </summary>
public static class KijiAppExtensions
{
    /// <summary>
    /// Generates an RSS feed from a keyed content collection. Each item's route is resolved
    /// from its <see cref="KijiApp.MapContent{TPage, TContent}"/> association; items without
    /// a mapped page are skipped. Entries appear in collection order.
    /// </summary>
    /// <param name="app">The Kiji application.</param>
    /// <param name="collection">The keyed content collection to build the feed from.</param>
    /// <param name="item">Projects a content item into its feed metadata.</param>
    /// <param name="path">The output path relative to the output directory.</param>
    public static KijiApp MapFeed<TContent>(
        this KijiApp app,
        ContentCollection<TContent> collection,
        Func<TContent, FeedItem> item,
        string path = "feed.xml")
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.MapArtifact(new RssFeedArtifact<TContent>(collection, item, path));
    }
}
