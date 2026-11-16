using Kiji.Assets;

namespace Kiji.Tests.TestSite.PageServices;

/// <summary>Changes the output bytes while retaining its recipe identity and filename.</summary>
public sealed class VaryingImageProcessor : IImageProcessor
{
    public string CacheIdentity => "varying-svg-v1";
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public async Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _calls);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "image.svg"),
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2\" height=\"2\"><title>{version}</title></svg>", cancellationToken);
        return new ProcessedImageInfo { OriginalWidth = 2, OriginalHeight = 2, Variants = [new("image.svg", 2)] };
    }
}
