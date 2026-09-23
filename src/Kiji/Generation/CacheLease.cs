namespace Kiji.Generation;

/// <summary>Serializes transactions across processes sharing a cache directory.</summary>
internal static class CacheLease
{
    internal static async Task<FileStream> AcquireAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory, "build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(50, cancellationToken); }
        }
    }
}
