namespace Kiji.Generation;

/// <summary>Immutable, verified image blobs and atomic cache publication.</summary>
internal sealed class ArtifactCache(string root)
{
    private static readonly Lock[] Gates = [.. Enumerable.Range(0, 64).Select(static _ => new Lock())];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _stored = new(StringComparer.Ordinal);
    internal static bool IsHash(string? hash) => hash is { Length: 64 }
        && hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private string BlobPath(string hash)
    {
        return !IsHash(hash)
            ? throw new InvalidDataException("Invalid cache artifact identity.")
            : Path.Combine(root, "images", hash);
    }

    internal string Store(string source)
    {
        var bytes = File.ReadAllBytes(source);
        var hash = BuildFingerprint.HashBytes(bytes);
        var path = BlobPath(hash);
        lock (Gates[hash[0] % Gates.Length])
        {
            if (_stored.ContainsKey(path)) { return hash; }
            if (!File.Exists(path) || BuildFingerprint.HashFile(path) != hash) { WriteAtomic(path, bytes); }
            _stored.TryAdd(path, 0);
            return hash;
        }
    }

    internal void EnsureStored(string hash, string source)
    {
        var path = BlobPath(hash);
        // The stage was verified by the planner. An existing blob is not consumed
        // here: its bytes will be checked if a later build actually restores it.
        if (_stored.ContainsKey(path) || File.Exists(path)) { return; }
        lock (Gates[hash[0] % Gates.Length])
        {
            if (BuildFingerprint.HashFile(path) != hash && Store(source) != hash)
            {
                throw new IOException("An output changed during the build.");
            }
            _stored.TryAdd(path, 0);
        }
    }

    internal bool Restore(string hash, string output)
    {
        if (!IsHash(hash)) { return false; }
        lock (Gates[hash[0] % Gates.Length])
        {
            if (File.Exists(output) && BuildFingerprint.HashFile(output) == hash) { return true; }
            try
            {
                var path = BlobPath(hash);
                var bytes = File.ReadAllBytes(path);
                if (BuildFingerprint.HashBytes(bytes) != hash) { return false; }
                _stored.TryAdd(path, 0);
                // Publish staging is private until the entire build succeeds. Like
                // freshly rendered HTML, restored output need not be renamed twice.
                // Persistent blobs and the manifest still use atomic replacement.
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllBytes(output, bytes);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    internal static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal void Collect(IEnumerable<string> references)
    {
        var directory = Path.Combine(root, "images");
        if (!Directory.Exists(directory)) { return; }
        var live = references.ToHashSet(StringComparer.Ordinal);
        // Nested directories belong to the retired directory-addressed image cache.
        var fullDirectory = Path.GetFullPath(directory);
        foreach (var child in Directory.EnumerateDirectories(fullDirectory))
        {
            if (!Path.GetFullPath(child).StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Cache cleanup escaped its root.");
            }
            Directory.Delete(child, recursive: true);
        }
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (!live.Contains(Path.GetFileName(path))) { File.Delete(path); }
        }
    }
}
