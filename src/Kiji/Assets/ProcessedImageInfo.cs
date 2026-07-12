namespace Kiji.Assets;

/// <summary>
/// A single generated variant of a processed image.
/// </summary>
/// <param name="FileName">The variant file name, relative to the directory it was written to.</param>
/// <param name="Width">The pixel width of the variant.</param>
public sealed record ImageVariant(string FileName, int Width);

/// <summary>
/// The result of processing one source image: its intrinsic dimensions and the
/// generated variants, ascending by width. The largest variant is the default source.
/// </summary>
public sealed record ProcessedImageInfo
{
    /// <summary>
    /// The width of the source image in pixels.
    /// </summary>
    public required int OriginalWidth { get; init; }

    /// <summary>
    /// The height of the source image in pixels.
    /// </summary>
    public required int OriginalHeight { get; init; }

    /// <summary>
    /// The generated variants, ascending by width.
    /// </summary>
    public required IReadOnlyList<ImageVariant> Variants { get; init; }
}
