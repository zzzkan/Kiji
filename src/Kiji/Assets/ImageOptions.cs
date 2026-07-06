namespace Kiji.Assets;

/// <summary>
/// Options controlling responsive image generation.
/// </summary>
public sealed class ImageOptions
{
    /// <summary>
    /// Target widths for responsive variants. Variants are generated only when
    /// smaller than the source image width.
    /// </summary>
    public IReadOnlyList<int> Widths { get; set; } = [320, 640, 960, 1280];

    /// <summary>
    /// Maximum width for the variant generated at the source image size.
    /// </summary>
    public int MaxSourceWidth { get; set; } = 1920;

    /// <summary>
    /// Encoding quality (0-100) for generated variants.
    /// </summary>
    public int Quality { get; set; } = 80;
}
