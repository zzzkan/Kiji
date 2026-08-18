namespace Kiji.Feeds;

/// <summary>
/// RSS feed support for <see cref="KijiApp"/>.
/// </summary>
public static class RssFeedKijiAppExtensions
{
    /// <summary>
    /// Generates an RSS feed from the entries the factory returns, in the order it
    /// returns them. The factory is evaluated once per site snapshot.
    /// </summary>
    /// <param name="app">The Kiji application.</param>
    /// <param name="items">
    /// Produces the feed entries from the app's services — resolve a
    /// <see cref="ContentDictionary{T}"/> here and sort as the feed should read, newest
    /// first by convention.
    /// </param>
    /// <param name="path">The output path relative to the output directory.</param>
    public static KijiApp MapFeed(
        this KijiApp app,
        Func<IServiceProvider, IEnumerable<FeedItem>> items,
        string path = "feed.xml")
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.MapArtifact(new RssFeedArtifact(items, path));
    }
}
