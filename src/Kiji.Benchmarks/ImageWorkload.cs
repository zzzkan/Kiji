using Kiji.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
            using var image = new Image<Rgba32>(width, width * 3 / 4);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)(x * 255 / width), (byte)(y * 255 / accessor.Height), (byte)((x + y + i) % 256));
                    }
                }
            });
            await image.SaveAsPngAsync(_sources[i]);
        }
    }

    internal void Reset(bool cold)
    {
        if (cold && Directory.Exists(Cache)) { Directory.Delete(Cache, true); }
        if (Directory.Exists(Output)) { Directory.Delete(Output, true); }
    }

    internal Task RunAsync(IImageAssetProcessor processor) => Task.WhenAll(_sources.Select((source, i) =>
        processor.ProcessImageAsync(source, Path.Combine(Output, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Cache)));

    internal async Task RunSequentialAsync(IImageAssetProcessor processor)
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            await processor.ProcessImageAsync(_sources[i], Path.Combine(Output, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Cache);
        }
    }

    internal Dictionary<string, byte[]> OutputBytes() => Directory.GetFiles(Output, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(Output, p), File.ReadAllBytes);

    public void Dispose()
    {
        Directory.Delete(Root, true);
    }
}
