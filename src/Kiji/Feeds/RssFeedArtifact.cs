using System.Globalization;
using System.Text;
using System.Xml;

namespace Kiji.Feeds;

/// <summary>
/// Generates an RSS 2.0 feed from a sequence of entries, written in the order given.
/// </summary>
public sealed class RssFeedArtifact : ISiteArtifact
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    private readonly Func<IServiceProvider, IEnumerable<FeedItem>> _items;

    /// <param name="items">Produces the feed entries, in the order they should appear.</param>
    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    public RssFeedArtifact(Func<IServiceProvider, IEnumerable<FeedItem>> items, string outputRelativePath = "feed.xml")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

        _items = items;
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

            foreach (var item in _items(context.Services))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemUrl = site.BaseUrl.AppendRelativePath(item.RoutePath).AbsoluteUri;
                // RFC 1123 date; converting to UTC keeps the offset correct for any zone.
                var pubDate = item.PublishedAt.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

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
