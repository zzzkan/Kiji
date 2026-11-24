using System.Collections.Concurrent;

namespace Kiji.Incremental;

internal sealed class ContentFileRegistry
{
    private readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.Ordinal);

    internal string? GetSnapshotHash(string path) => _hashes.GetValueOrDefault(path);

    internal void Record(FileInfo file, string hash) => _hashes[file.FullName] = hash;

    internal void Invalidate() => _hashes.Clear();
}
