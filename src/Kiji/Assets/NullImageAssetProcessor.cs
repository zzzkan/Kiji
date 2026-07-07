namespace Kiji.Assets;

/// <summary>
/// Default <see cref="IImageAssetProcessor"/> used when no image backend is registered.
/// Performs no optimization; content keeps its original image references.
/// </summary>
public sealed class NullImageAssetProcessor : IImageAssetProcessor
{
    public Task<IReadOnlyDictionary<string, ProcessedImageInfo>> ProcessReferencedImagesAsync(
        string outputDirectory,
        string sourceDirectory,
        IEnumerable<string> imageUrls,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, ProcessedImageInfo>>(
            new Dictionary<string, ProcessedImageInfo>(StringComparer.Ordinal));
    }
}
