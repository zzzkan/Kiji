using System.Globalization;
using System.Text;
using System.Xml;
using Kiji.Rendering;

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
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";

    private readonly ContentCollection<TContent> _collection;
    private readonly Func<TContent, CancellationToken, Task<FeedItem>> _itemSelector;

    /// <param name="collection">The keyed content collection to build the feed from.</param>
    /// <param name="itemSelector">Projects a content item into its feed metadata.</param>
    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    public RssFeedArtifact(
        ContentCollection<TContent> collection,
        Func<TContent, FeedItem> itemSelector,
        string outputRelativePath = "feed.xml")
        : this(
            collection,
            itemSelector is null
                ? null!
                : (content, _) => Task.FromResult(itemSelector(content)),
            outputRelativePath)
    {
        ArgumentNullException.ThrowIfNull(itemSelector);
    }

    /// <param name="collection">The keyed content collection to build the feed from.</param>
    /// <param name="itemSelector">
    /// Asynchronously projects a content item into its feed metadata, e.g. to render
    /// the full entry HTML for <c>content:encoded</c>.
    /// </param>
    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    public RssFeedArtifact(
        ContentCollection<TContent> collection,
        Func<TContent, CancellationToken, Task<FeedItem>> itemSelector,
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
            await writer.WriteAttributeStringAsync("xmlns", "content", ns: null, ContentNamespace);

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

                if (!context.TryResolvePage(_collection.GetKey(content), out var page))
                {
                    continue;
                }

                var item = await SelectItemAsync(content, page, cancellationToken);
                var itemUrl = site.BaseUrl.AppendRelativePath(page.RoutePath).AbsoluteUri;
                // RFC 1123 date; converting to UTC keeps the offset correct for any zone.
                var pubDate = item.PublishedAt.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

                await writer.WriteStartElementAsync(prefix: null, "item", ns: null);
                await writer.WriteElementStringAsync(prefix: null, "title", ns: null, item.Title);
                await writer.WriteElementStringAsync(prefix: null, "link", ns: null, itemUrl);
                await writer.WriteElementStringAsync(prefix: null, "guid", ns: null, itemUrl);
                await writer.WriteElementStringAsync(prefix: null, "pubDate", ns: null, pubDate);
                await writer.WriteElementStringAsync(prefix: null, "description", ns: null, item.Description);
                if (!string.IsNullOrEmpty(item.ContentHtml))
                {
                    await writer.WriteElementStringAsync("content", "encoded", ContentNamespace, item.ContentHtml);
                }

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        }
    }

    private async Task<FeedItem> SelectItemAsync(TContent content, SitePageInfo page, CancellationToken cancellationToken)
    {
        // Run the selector under the item's page render context so content renderers
        // (e.g. MarkdownContent.RenderAsync for content:encoded) resolve the same
        // per-page cache and output location as the page render itself.
        PageRenderContext.SetCurrent(new PageRenderContext
        {
            RoutePath = page.RoutePath,
            OutputRelativeDirectory = Path.GetDirectoryName(page.OutputRelativePath) ?? string.Empty,
        });

        try
        {
            return await _itemSelector(content, cancellationToken);
        }
        finally
        {
            PageRenderContext.SetCurrent(null);
        }
    }
}
