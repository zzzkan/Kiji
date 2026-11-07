using System.Globalization;
using System.Text;
using System.Xml;

namespace Kiji.Feeds;

/// <summary>
/// Generates an RSS 2.0 feed from a sequence of entries, written in the order given.
/// </summary>
internal sealed class RssFeedArtifact(
    Func<IServiceProvider, IEnumerable<FeedItem>> items,
    string outputRelativePath = "feed.xml")
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    /// <inheritdoc/>
    public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var site = context.Site;
        // Synchronous XML into memory, then one asynchronous copy: an asynchronous
        // XmlWriter awaits every element, which costs more than the feed's I/O.
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, Settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement(prefix: null, "rss", ns: null);
            writer.WriteAttributeString(prefix: null, "version", ns: null, "2.0");
            writer.WriteAttributeString("xmlns", "atom", ns: null, AtomNamespace);

            writer.WriteStartElement(prefix: null, "channel", ns: null);
            writer.WriteElementString(prefix: null, "title", ns: null, site.Name);
            writer.WriteElementString(prefix: null, "link", ns: null, site.BaseUrl.AbsoluteUri);
            writer.WriteElementString(prefix: null, "description", ns: null, site.Description);
            writer.WriteElementString(prefix: null, "language", ns: null, site.Language);

            writer.WriteStartElement("atom", "link", AtomNamespace);
            writer.WriteAttributeString(prefix: null, "href", ns: null, site.ResolveUrl(outputRelativePath).AbsoluteUri);
            writer.WriteAttributeString(prefix: null, "rel", ns: null, "self");
            writer.WriteAttributeString(prefix: null, "type", ns: null, "application/rss+xml");
            writer.WriteEndElement();

            foreach (var item in items(context.Services))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemUrl = site.ResolveUrl(item.RelativePath).AbsoluteUri;
                // RFC 1123 date; converting to UTC keeps the offset correct for any zone.
                var pubDate = item.PublishedAt.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

                writer.WriteStartElement(prefix: null, "item", ns: null);
                writer.WriteElementString(prefix: null, "title", ns: null, item.Title);
                writer.WriteElementString(prefix: null, "link", ns: null, itemUrl);
                writer.WriteElementString(prefix: null, "guid", ns: null, itemUrl);
                writer.WriteElementString(prefix: null, "pubDate", ns: null, pubDate);
                writer.WriteElementString(prefix: null, "description", ns: null, item.Description);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        await output.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken);
    }
}
