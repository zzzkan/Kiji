using Kiji.Generation;

namespace Kiji.Sitemaps;

/// <summary>
/// Generates a sitemap from generated pages. URLs are sorted for deterministic output.
/// </summary>
internal sealed class SitemapArtifact
{
    private const string NotFoundRelativePath = "404.html";
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    // Validation is independent of the base URL, so registration can reject a path the
    // site would fail to resolve.
    private static readonly SiteInfo ValidationSite = new() { BaseUrl = new Uri("https://localhost/"), Name = "validation" };

    private readonly List<string> _excludedPaths = [NotFoundRelativePath];

    /// <param name="excludedPaths">Site-relative page paths to omit in addition to <c>404.html</c>.</param>
    public SitemapArtifact(IEnumerable<string>? excludedPaths = null)
    {
        if (excludedPaths is null)
        {
            return;
        }

        foreach (var path in excludedPaths)
        {
            RelativePath.Validate(path, nameof(excludedPaths));
            try
            {
                ValidationSite.ResolveUrl(path);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"Excluded path '{path}' is not a valid site-relative path. {exception.Message}", nameof(excludedPaths), exception);
            }

            _excludedPaths.Add(path);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        // Route parameter values are percent-encoded in page paths while literal segments are
        // not, so compare resolved URLs: an exclusion matches in either spelling.
        var site = context.Site;
        var excludedUrls = _excludedPaths
            .Select(path => site.ResolveUrl(path).AbsoluteUri)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pages = context.Pages
            .Select(page => (page.RelativePath, Url: site.ResolveUrl(page.RelativePath).AbsoluteUri))
            .Where(page => !excludedUrls.Contains(page.Url))
            .OrderBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase);

        using var xml = new ChunkedXmlOutput(output);
        var writer = xml.Writer;
        writer.WriteStartDocument();
        writer.WriteStartElement(prefix: null, "urlset", SitemapNamespace);

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.WriteStartElement(prefix: null, "url", ns: null);
            writer.WriteElementString(prefix: null, "loc", ns: null, page.Url);
            writer.WriteEndElement();
            await xml.WriteChunkAsync(cancellationToken);
        }

        writer.WriteEndElement();
        await xml.CompleteAsync(cancellationToken);
    }
}
