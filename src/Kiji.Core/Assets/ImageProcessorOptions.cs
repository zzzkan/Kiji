namespace Kiji.Assets;

/// <summary>Settings of the default WebP processor; a replaced <see cref="IImageProcessor"/> ignores them.</summary>
public sealed class ImageProcessorOptions
{
    /// <summary>Widths of the smaller variants; null means 320, 640, 960 and 1280.</summary>
    /// <remarks>Widths not below the image or <see cref="MaxWidth"/> are skipped; the image's own width is always added.</remarks>
    public IReadOnlyList<int>? Widths { get; set; }

    /// <summary>The widest variant in pixels. Defaults to 1920.</summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>WebP quality from 1 to 100. Defaults to 80.</summary>
    public int Quality { get; set; } = 80;
}
