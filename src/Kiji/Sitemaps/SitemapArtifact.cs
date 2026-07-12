using System.Globalization;
using System.Text;
using System.Xml;

namespace Kiji.Sitemaps;

/// <summary>
/// Generates a sitemap from every generated page, excluding pages marked
/// with <c>ExcludeFromSitemap</c>. Pages with a known last-modification timestamp
/// emit <c>lastmod</c>. URLs are sorted for deterministic output.
/// </summary>
public sealed class SitemapArtifact : ISiteArtifact
{
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    public SitemapArtifact(string outputRelativePath = "sitemap.xml")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

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

        var pages = context.Pages
            .Where(static page => !page.ExcludeFromSitemap)
            .OrderBy(static page => page.RoutePath, StringComparer.OrdinalIgnoreCase);

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
            await writer.WriteStartElementAsync(prefix: null, "urlset", SitemapNamespace);

            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await writer.WriteStartElementAsync(prefix: null, "url", ns: null);
                await writer.WriteElementStringAsync(prefix: null, "loc", ns: null, context.Site.BaseUrl.AppendRelativePath(page.RoutePath).AbsoluteUri);
                if (page.LastModified is { } lastModified)
                {
                    await writer.WriteElementStringAsync(
                        prefix: null,
                        "lastmod",
                        ns: null,
                        lastModified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                }

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        }
    }
}
