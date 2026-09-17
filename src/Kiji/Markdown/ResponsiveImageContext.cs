using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document state for responsive image rendering: the processed image lookup
/// and the image counter driving eager/lazy loading.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public int ImageCount;
}
