namespace Kiji.Assets;

/// <summary>Generates optimized image variants in a page output directory.</summary>
/// <remarks>
/// Register a singleton in StaticSiteBuilder.Services to replace the default processor.
/// Implementations must support concurrent calls and retain no page or content state.
/// Standard DI ownership applies; directly registered instances are disposed by the caller.
/// </remarks>
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
