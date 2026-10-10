using Kiji.Assets;
using SkiaSharp;

namespace Kiji.Benchmarks;

internal sealed class ImageWorkload : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"kiji-image-bench-{Guid.NewGuid():N}");
    private string[] _sources = [];
    private string Cache => Path.Combine(Root, "cache");
    private string Output => Path.Combine(Root, "output");

    internal async Task SetupAsync(int width, bool shared)
    {
        Directory.CreateDirectory(Root);
        _sources = new string[4];
        for (var i = 0; i < _sources.Length; i++)
        {
            _sources[i] = Path.Combine(Root, $"source-{(shared ? 0 : i)}.png");
            if (File.Exists(_sources[i])) { continue; }
            using var image = new SKBitmap(width, width * 3 / 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    image.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / image.Height), (byte)((x + y + i) % 256)));
                }
            }
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(_sources[i], encoded.ToArray());
        }
    }

    internal void Reset(bool cold)
    {
        if (cold && Directory.Exists(Cache)) { Directory.Delete(Cache, true); }
        if (Directory.Exists(Output)) { Directory.Delete(Output, true); }
    }

    internal Task RunAsync(IImageProcessor processor) => Task.WhenAll(_sources.Select((source, i) =>
        ImageArtifactProcessor.ProcessAsync(processor, source,
            Path.Combine(Output, i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Path.Combine(Cache, "images"), default)));

    public void Dispose()
    {
        Directory.Delete(Root, true);
    }
}
