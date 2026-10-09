using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Per-document image lookup and output URL directory for responsive rendering.
/// </summary>
internal sealed class ResponsiveImageContext(
    IReadOnlyDictionary<string, ProcessedImageInfo> imageInfoLookup,
    string outputUrlDirectory)
{
    public IReadOnlyDictionary<string, ProcessedImageInfo> ImageInfoLookup { get; } = imageInfoLookup;

    public string OutputUrlDirectory { get; } = outputUrlDirectory;
}
