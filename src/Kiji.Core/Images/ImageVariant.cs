namespace Kiji.Images;

/// <summary>One generated variant.</summary>
/// <param name="FileName">The file name in the output directory.</param>
/// <param name="Width">The width in pixels.</param>
public sealed record ImageVariant(string FileName, int Width);
