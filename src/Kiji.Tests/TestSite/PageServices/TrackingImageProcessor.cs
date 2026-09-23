using Kiji.Assets;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class TrackingImageProcessor : IImageProcessor, IAsyncDisposable
{
    public string CacheIdentity => "tracking-svg-v1";
    private int _calls;
    public int Calls => _calls;
    public int DisposeCount { get; private set; }

    public async Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "custom.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2\" height=\"2\"></svg>", cancellationToken);
        return new ProcessedImageInfo { OriginalWidth = 2, OriginalHeight = 2, Variants = [new("custom.svg", 2)] };
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        DisposeCount++;
    }
}
