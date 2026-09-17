namespace Kiji.Assets;

/// <summary>Generates optimized image variants in a page output directory.</summary>
public interface IImageProcessor
{
    /// <summary>Produces variants of the source image and describes them.</summary>
    Task<ProcessedImageInfo> ProcessAsync(
        string sourceFilePath,
        string outputDirectory,
        string? cacheDirectory = null,
        CancellationToken cancellationToken = default);
}
