namespace Kiji.Assets;

/// <summary>
/// Options controlling responsive image generation.
/// </summary>
public sealed class ImageOptions
{
    /// <summary>
    /// Responsive widths smaller than the source, defaulting to 320, 640, 960, and 1280 pixels.
    /// </summary>
    public IReadOnlyList<int> Widths { get; set; } = [320, 640, 960, 1280];

    /// <summary>
    /// Maximum width for the source-size variant, defaulting to 1920 pixels.
    /// </summary>
    public int MaxSourceWidth { get; set; } = 1920;

    /// <summary>
    /// Encoding quality from 0 to 100, defaulting to 80.
    /// </summary>
    public int Quality { get; set; } = 80;
}
