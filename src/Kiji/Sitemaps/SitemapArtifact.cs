using Kiji.Generation;

namespace Kiji.Sitemaps;

/// <summary>
/// Generates a sitemap from generated pages. URLs are sorted for deterministic output.
/// </summary>
internal sealed class SitemapArtifact
{
    private const string NotFoundRelativePath = "404.html";
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly HashSet<string> _excludedPaths;

    /// <param name="excludedPaths">Site-relative page paths to omit in addition to <c>404.html</c>.</param>
    public SitemapArtifact(IEnumerable<string>? excludedPaths = null)
    {
        _excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NotFoundRelativePath,
        };

        if (excludedPaths is null)
        {
            return;
        }

        foreach (var path in excludedPaths)
        {
            RelativePath.Validate(path, nameof(excludedPaths));
            _excludedPaths.Add(path);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var pages = context.Pages
            .Where(page => !_excludedPaths.Contains(page.RelativePath))
            .OrderBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase);

        using var xml = new ChunkedXmlOutput(output);
        var writer = xml.Writer;
        writer.WriteStartDocument();
        writer.WriteStartElement(prefix: null, "urlset", SitemapNamespace);

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.WriteStartElement(prefix: null, "url", ns: null);
            var pageUrl = context.Site.ResolveUrl(page.RelativePath);
            writer.WriteElementString(prefix: null, "loc", ns: null, pageUrl.AbsoluteUri);
            writer.WriteEndElement();
            await xml.WriteChunkAsync(cancellationToken);
        }

        writer.WriteEndElement();
        await xml.CompleteAsync(cancellationToken);
    }
}
