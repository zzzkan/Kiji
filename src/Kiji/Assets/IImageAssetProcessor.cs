namespace Kiji.Assets;

/// <summary>Generates optimized image variants in a page output directory.</summary>
public interface IImageAssetProcessor
{
    /// <summary>
    /// Produces variants of the source image in the output directory and describes them.
    /// </summary>
    /// <param name="sourceFilePath">The absolute path of an existing source image.</param>
    /// <param name="outputDirectory">The directory the variants are written to; created as needed.</param>
    /// <param name="cacheDirectory">
    /// An optional persistent directory for reusing variants between builds.
    /// </param>
    Task<ProcessedImageInfo> ProcessImageAsync(
        string sourceFilePath,
        string outputDirectory,
        string? cacheDirectory = null,
        CancellationToken cancellationToken = default);
}
