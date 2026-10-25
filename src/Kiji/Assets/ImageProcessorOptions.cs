namespace Kiji.Assets;

/// <summary>Configures the default WebP image processor.</summary>
/// <remarks>
/// Configure it with <c>builder.Services.Configure&lt;ImageProcessorOptions&gt;(...)</c>. The values are validated
/// when the site starts running, and the processor reads them once. They do not apply to a replaced
/// <see cref="IImageProcessor"/>. Changing them re-encodes images on the next publish.
/// </remarks>
public sealed class ImageProcessorOptions
{
    /// <summary>The variant widths used when <see cref="Widths"/> is not set: 320, 640, 960 and 1280 pixels.</summary>
    public static IReadOnlyList<int> DefaultWidths { get; } = [320, 640, 960, 1280];

    /// <summary>Widths in pixels of the smaller variants, or <see langword="null"/> for <see cref="DefaultWidths"/>.</summary>
    /// <remarks>
    /// Widths at or above the source width or <see cref="MaxWidth"/> are skipped. Every image also gets
    /// a variant at its own width, capped at <see cref="MaxWidth"/>.
    /// </remarks>
    public IReadOnlyList<int>? Widths { get; set; }

    /// <summary>The widest variant in pixels. Wider images are scaled down to it. Defaults to 1920.</summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>The lossy WebP quality from 1 to 100. Defaults to 80.</summary>
    public int Quality { get; set; } = 80;
}
