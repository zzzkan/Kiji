namespace Kiji.Assets;

/// <summary>
/// Processes a single source image referenced by content into optimized variants
/// written to a page-relative output directory. Implementations own the image backend;
/// consumers only depend on the resulting <see cref="ProcessedImageInfo"/>.
/// </summary>
public interface IImageAssetProcessor
{
    /// <summary>
    /// Produces variants of the source image in the output directory and describes them.
    /// </summary>
    /// <param name="sourceFilePath">The absolute path of the source image; guaranteed to exist.</param>
    /// <param name="outputDirectory">The directory the variants are written to; created as needed.</param>
    /// <param name="cacheDirectory">
    /// Optional persistent cache directory. When set, variants are materialized there once
    /// and copied into <paramref name="outputDirectory"/>, so unchanged images are not re-encoded.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<ProcessedImageInfo> ProcessImageAsync(
        string sourceFilePath,
        string outputDirectory,
        string? cacheDirectory = null,
        CancellationToken cancellationToken = default);
}
