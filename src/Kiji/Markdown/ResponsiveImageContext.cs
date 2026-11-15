using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document shared image lookup and deployment base path.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ImageAsset> imageInfoLookup,
    string basePath)
{
    public IReadOnlyDictionary<string, ImageAsset> ImageInfoLookup { get; } = imageInfoLookup;

    public string BasePath { get; } = basePath;

    public int ImageCount { get; set; }
}
