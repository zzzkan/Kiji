namespace Kiji.Assets;

/// <summary>Generates optimized image variants in a page output directory.</summary>
public interface IImageProcessor
{
    /// <summary>Digest of the implementation and every transformation setting; null disables persistent reuse.</summary>
    string? CacheIdentity => null;

    /// <summary>Produces variants of the source image and describes them.</summary>
    Task<ProcessedImageInfo> ProcessAsync(
        string sourceFilePath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
