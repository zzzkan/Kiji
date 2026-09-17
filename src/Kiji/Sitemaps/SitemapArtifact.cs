using System.Text;
using System.Xml;

namespace Kiji.Sitemaps;

/// <summary>
/// Generates a sitemap from generated pages. URLs are sorted for deterministic output.
/// </summary>
internal sealed class SitemapArtifact
{
    private const string NotFoundRelativePath = "404.html";
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private readonly HashSet<string> _excludedPaths;

    /// <param name="outputRelativePath">The output path relative to the output directory.</param>
    /// <param name="excludedPaths">Site-relative page paths to omit in addition to <c>404.html</c>.</param>
    public SitemapArtifact(string outputRelativePath = "sitemap.xml", IEnumerable<string>? excludedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

        OutputRelativePath = outputRelativePath;
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
    public string OutputRelativePath { get; }

    /// <inheritdoc/>
    public async Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var pages = context.Pages
            .Where(page => !_excludedPaths.Contains(page.RelativePath))
            .OrderBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase);

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
                RelativePath.Validate(page.RelativePath, nameof(page.RelativePath));
                await writer.WriteElementStringAsync(prefix: null, "loc", ns: null, new Uri(context.Site.BaseUrl, page.RelativePath).AbsoluteUri);
                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
            await writer.FlushAsync();
        }
    }
}
