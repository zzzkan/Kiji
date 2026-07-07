namespace Kiji;

/// <summary>
/// A site-wide output file generated after all pages are rendered, e.g. an RSS feed
/// or a sitemap. Register implementations with <see cref="KijiApp.MapArtifact"/>.
/// </summary>
public interface ISiteArtifact
{
    /// <summary>
    /// The output path relative to the output directory, e.g. <c>feed.xml</c>.
    /// </summary>
    string OutputRelativePath { get; }

    /// <summary>
    /// Writes the artifact content to the output stream.
    /// </summary>
    /// <param name="output">The destination stream; owned by the caller.</param>
    /// <param name="context">The site snapshot the artifact is generated from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken);
}
