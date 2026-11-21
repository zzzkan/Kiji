namespace Kiji.Images;

internal interface IContentAddressedImageProcessor : IImageProcessor
{
    Task<ProcessedImageInfo> ProcessBytesAsync(byte[] bytes, string fileNameBase, string sourceHash,
        string outputDirectory, CancellationToken cancellationToken);
}
