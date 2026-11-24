namespace Kiji.Images;

/// <summary>Settings of the default WebP processor; a replaced <see cref="IImageProcessor"/> ignores them.</summary>
public sealed class ImageProcessorOptions
{
    /// <summary>Widths of the smaller variants; null means 320, 640, 960 and 1280.</summary>
    /// <remarks>The largest output width is always included, capped by the source size, <see cref="MaxWidth"/> and WebP dimensions.</remarks>
    public IReadOnlyList<int>? Widths { get; set; }

    /// <summary>The widest variant in pixels. Defaults to 1920.</summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>WebP quality from 1 to 100. Defaults to 80.</summary>
    public int Quality { get; set; } = 80;
}
