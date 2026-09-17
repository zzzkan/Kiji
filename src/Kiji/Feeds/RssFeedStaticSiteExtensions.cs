namespace Kiji.Feeds;

/// <summary>
/// RSS feed support for <see cref="StaticSite"/>.
/// </summary>
public static class RssFeedStaticSiteExtensions
{
    /// <summary>Registers an RSS feed preserving the order of the supplied entries.</summary>
    /// <param name="items">A deferred factory evaluated when the feed is generated.</param>
    /// <param name="path">The output-relative path, defaulting to <c>feed.xml</c>.</param>
    public static StaticSite AddRssFeed(
        this StaticSite app,
        Func<IServiceProvider, IEnumerable<FeedItem>> items,
        string path = "feed.xml")
    {
        ArgumentNullException.ThrowIfNull(app);

        var artifact = new RssFeedArtifact(items, path);
        return app.AddArtifact(artifact.OutputRelativePath, artifact.WriteAsync);
    }
}
