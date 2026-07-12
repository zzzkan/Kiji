namespace Kiji.Assets;

/// <summary>
/// A single generated variant of a processed image.
/// </summary>
/// <param name="FileName">The variant file name, relative to the directory it was written to.</param>
/// <param name="Width">The pixel width of the variant.</param>
public sealed record ImageVariant(string FileName, int Width);
