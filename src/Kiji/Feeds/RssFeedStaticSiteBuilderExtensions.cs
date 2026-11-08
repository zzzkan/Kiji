using Microsoft.Extensions.Hosting;
namespace Kiji.Feeds;

/// <summary>
/// RSS feed support for <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class RssFeedStaticSiteBuilderExtensions
{
    /// <summary>Adds an RSS feed preserving the order of the supplied entries.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="items">A deferred factory evaluated when the feed is generated.</param>
    /// <param name="path">The output-relative path, defaulting to <c>feed.xml</c>.</param>
    public static IHostApplicationBuilder AddRssFeed(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, IEnumerable<FeedItem>> items,
        string path = "feed.xml")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(items);

        var artifact = new RssFeedArtifact(items, path);
        return builder.AddArtifact(path, artifact.WriteAsync, preserveUnchangedOutput: true);
    }
}
