namespace Kiji.Assets;

/// <summary>
/// Processes images referenced by content and produces optimized asset variants.
/// Implementations own the image backend; consumers only depend on the resulting
/// <see cref="ProcessedImageInfo"/> lookup.
/// </summary>
public interface IImageAssetProcessor
{
    /// <summary>
    /// Processes the referenced images and generates optimized variants in the output directory.
    /// </summary>
    /// <param name="outputDirectory">The directory where generated variants are written.</param>
    /// <param name="sourceDirectory">The directory containing the source images.</param>
    /// <param name="imageUrls">Image URLs as referenced by the content (relative paths).</param>
    /// <returns>A lookup from image reference key to the processed image information.</returns>
    Task<IReadOnlyDictionary<string, ProcessedImageInfo>> ProcessReferencedImagesAsync(
        string outputDirectory,
        string sourceDirectory,
        IEnumerable<string> imageUrls,
        CancellationToken cancellationToken = default);
}
