using Kiji.Assets;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Images;

/// <summary>
/// ImageSharp-based image optimization for <see cref="KijiBuilder"/>.
/// </summary>
public static class KijiBuilderExtensions
{
    /// <summary>
    /// Registers responsive image optimization (WebP variants with srcset support)
    /// as the site's <see cref="IImageAssetProcessor"/>.
    /// </summary>
    public static KijiBuilder AddImageOptimization(this KijiBuilder builder, Action<ImageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ImageOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton<IImageAssetProcessor>(new ImageProcessor(options));
        return builder;
    }
}
