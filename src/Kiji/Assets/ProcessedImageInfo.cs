namespace Kiji.Assets;

/// <summary>
/// Represents information about a processed image.
/// </summary>
public sealed class ProcessedImageInfo
{
    /// <summary>
    /// Gets or sets the asset file name base used for generated WebP variants.
    /// </summary>
    public required string AssetFileNameBase { get; init; }

    /// <summary>
    /// Gets or sets the original file name without extension.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or sets the original width of the image in pixels.
    /// </summary>
    public required int OriginalWidth { get; init; }

    /// <summary>
    /// Gets or sets the original height of the image in pixels.
    /// </summary>
    public required int OriginalHeight { get; init; }

    /// <summary>
    /// Gets or sets the available widths for generated responsive images.
    /// </summary>
    public required int[] AvailableWidths { get; init; }

    /// <summary>
    /// Gets or sets the aspect ratio of the image (width / height).
    /// </summary>
    public required double AspectRatio { get; init; }

    /// <summary>
    /// Gets or sets the content hash of the original image (8 characters from SHA256).
    /// </summary>
    public required string ContentHash { get; init; }
}
