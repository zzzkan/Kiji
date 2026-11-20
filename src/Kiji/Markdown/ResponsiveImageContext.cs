using Kiji.Assets;

namespace Kiji.Markdown;

internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string basePath)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public string BasePath { get; } = basePath;

    public int ImageCount { get; set; }
}
