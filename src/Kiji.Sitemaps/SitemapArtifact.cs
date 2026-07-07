using System.Text;
using System.Xml;

namespace Kiji.Sitemaps;

/// <summary>
/// Generates a sitemap from every generated page, excluding pages marked
/// with <c>ExcludeFromSitemap</c>. URLs are sorted for deterministic output.
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

        var urls = context.Pages
            .Where(static page => !page.ExcludeFromSitemap)
            .Select(static page => page.RoutePath)
            .OrderBy(static route => route, StringComparer.OrdinalIgnoreCase);

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

            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await writer.WriteStartElementAsync(prefix: null, "url", ns: null);
                await writer.WriteElementStringAsync(prefix: null, "loc", ns: null, context.Site.BaseUrl.AppendRelativePath(url).AbsoluteUri);
                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        }
    }
}
