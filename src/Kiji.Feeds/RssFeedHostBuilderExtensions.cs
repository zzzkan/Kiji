using Microsoft.Extensions.Hosting;

namespace Kiji.Feeds;

/// <summary>Adds RSS feeds to a site.</summary>
public static class RssFeedHostBuilderExtensions
{
    /// <summary>Adds an RSS feed with the entries in the given order.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="items">Called when the feed is written, after all pages.</param>
    /// <param name="path">The output path; <c>feed.xml</c> by default.</param>
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
