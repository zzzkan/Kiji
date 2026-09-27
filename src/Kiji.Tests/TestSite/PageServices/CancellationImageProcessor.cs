using Kiji.Assets;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class CancellationImageProcessor(Func<CancellationToken, Task> beforeWrite) : IImageProcessor, IDisposable
{
    public string CacheIdentity => "cancellation-test-svg-v1";
    public int Calls { get; private set; }
    public bool Active { get; private set; }
    public bool Disposed { get; private set; }
    public bool DisposedWhileActive { get; private set; }

    public async Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Active = true;
        try
        {
            await beforeWrite(cancellationToken);
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "result.svg"), "<svg/>", cancellationToken);
            return new ProcessedImageInfo { OriginalWidth = 2, OriginalHeight = 2, Variants = [new("result.svg", 2)] };
        }
        finally { Active = false; }
    }

    public void Dispose()
    {
        DisposedWhileActive = Active;
        Disposed = true;
    }
}
