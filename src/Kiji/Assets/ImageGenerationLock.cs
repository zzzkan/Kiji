namespace Kiji.Assets;

/// <summary>Reference-counted gates include waiters, so a live key never gets two gates.</summary>
internal static class ImageGenerationLock
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, ImageGenerationLease> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static async ValueTask<ImageGenerationLease> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(path);
        ImageGenerationLease gate;
        lock (Sync)
        {
            if (!Gates.TryGetValue(key, out gate!))
            {
                gate = new ImageGenerationLease(key);
                Gates.Add(key, gate);
            }
            gate.References++;
        }
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            return gate;
        }
        catch
        {
            ReleaseReference(gate);
            throw;
        }
    }

    internal static void ReleaseReference(ImageGenerationLease gate)
    {
        lock (Sync)
        {
            if (--gate.References == 0)
            {
                Gates.Remove(gate.Key);
                gate.Semaphore.Dispose();
            }
        }
    }
}
