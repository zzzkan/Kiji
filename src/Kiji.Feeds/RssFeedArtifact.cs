using System.Globalization;
using System.Text;
using System.Xml;

namespace Kiji.Feeds;

/// <summary>
/// Generates an RSS 2.0 feed from a keyed content collection. Each item's route is
/// resolved from its page mapping; items without a mapped page are skipped. Entries
/// appear in collection order.
/// </summary>
public sealed class RssFeedArtifact<TContent> : ISiteArtifact
    where TContent : class
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    private readonly ContentCollection<TContent> _collection;
    private readonly Func<TContent, FeedItem> _itemSelector;

    /// <param name="collection">The keyed content collection to build the feed from.</param>
    /// <param name="itemSelector">Projects a content item into its feed metadata.</param>
    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    public RssFeedArtifact(
        ContentCollection<TContent> collection,
        Func<TContent, FeedItem> itemSelector,
        string outputRelativePath = "feed.xml")
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(itemSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

        _collection = collection;
        _itemSelector = itemSelector;
        OutputRelativePath = outputRelativePath;
    }

    /// <inheritdoc/>
    public string OutputRelativePath { get; }

    /// <inheritdoc/>
    public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var site = context.Site;
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        };

        var writer = XmlWriter.Create(output, settings);
        await using (writer)
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(prefix: null, "rss", ns: null);
            await writer.WriteAttributeStringAsync(prefix: null, "version", ns: null, "2.0");
            await writer.WriteAttributeStringAsync("xmlns", "atom", ns: null, AtomNamespace);

            await writer.WriteStartElementAsync(prefix: null, "channel", ns: null);
            await writer.WriteElementStringAsync(prefix: null, "title", ns: null, site.Name);
            await writer.WriteElementStringAsync(prefix: null, "link", ns: null, site.BaseUrl.AbsoluteUri);
            await writer.WriteElementStringAsync(prefix: null, "description", ns: null, site.Description);
            await writer.WriteElementStringAsync(prefix: null, "language", ns: null, site.Language);

            await writer.WriteStartElementAsync("atom", "link", AtomNamespace);
            await writer.WriteAttributeStringAsync(prefix: null, "href", ns: null, site.BaseUrl.AppendRelativePath(OutputRelativePath).AbsoluteUri);
            await writer.WriteAttributeStringAsync(prefix: null, "rel", ns: null, "self");
            await writer.WriteAttributeStringAsync(prefix: null, "type", ns: null, "application/rss+xml");
            await writer.WriteEndElementAsync();

            foreach (var content in _collection.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!context.TryResolveRoute(_collection.GetKey(content), out var routePath))
                {
                    continue;
                }

                var item = _itemSelector(content);
                var itemUrl = site.BaseUrl.AppendRelativePath(routePath).AbsoluteUri;
                var pubDate = item.PublishedAt.ToString("ddd, dd MMM yyyy HH:mm:ss zz00", CultureInfo.InvariantCulture);

                await writer.WriteStartElementAsync(prefix: null, "item", ns: null);
                await writer.WriteElementStringAsync(prefix: null, "title", ns: null, item.Title);
                await writer.WriteElementStringAsync(prefix: null, "link", ns: null, itemUrl);
                await writer.WriteElementStringAsync(prefix: null, "guid", ns: null, itemUrl);
                await writer.WriteElementStringAsync(prefix: null, "pubDate", ns: null, pubDate);
                await writer.WriteElementStringAsync(prefix: null, "description", ns: null, item.Description);
                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        }
    }
}
