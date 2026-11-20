namespace Kiji.Assets;

/// <summary>Writes image variants. Register a singleton in builder.Services to replace the default.</summary>
/// <remarks>Calls run concurrently; keep no page or content state.</remarks>
public interface IImageProcessor
{
    /// <summary>Changes whenever output could change; null disables caching.</summary>
    string? CacheIdentity => null;

    /// <summary>Writes variants of the source into the output directory.</summary>
    Task<ProcessedImageInfo> ProcessAsync(
        string sourceFilePath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
