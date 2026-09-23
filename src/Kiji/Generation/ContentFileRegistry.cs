using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>
/// Hashes of the exact bytes parsed in the current content snapshot. Invalidated
/// together with content before each publish or development reload; timestamps
/// never establish equivalence across snapshots.
/// </summary>
internal sealed class ContentFileRegistry
{
    private readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    internal string? GetSnapshotHash(string path) => _hashes.GetValueOrDefault(path);

    internal void Record(FileInfo file, string hash) => _hashes[file.FullName] = hash;

    internal void Invalidate() => _hashes.Clear();
}
