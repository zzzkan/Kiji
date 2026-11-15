using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document image lookup and the shared image directory's public URL.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string basePath)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public string BasePath { get; } = basePath;

    public int ImageCount { get; set; }
}
