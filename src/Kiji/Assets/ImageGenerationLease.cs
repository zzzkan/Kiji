namespace Kiji.Assets;

internal sealed class ImageGenerationLease(string key) : IDisposable
{
    internal string Key { get; } = key;
    internal SemaphoreSlim Semaphore { get; } = new(1, 1);
    internal int References { get; set; }

    public void Dispose()
    {
        Semaphore.Release();
        ImageGenerationLock.ReleaseReference(this);
    }
}
