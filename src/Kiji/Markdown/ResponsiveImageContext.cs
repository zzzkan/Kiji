using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document image lookup (site-relative variant paths) and deployment base path.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string basePath)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public string BasePath { get; } = basePath;

    public int ImageCount { get; set; }
}
