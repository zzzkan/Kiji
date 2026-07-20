using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>
/// Shares content-file hashes computed during materialization with the incremental
/// build planner, so a markdown file that was already read for parsing is never
/// re-read just to be fingerprinted. Entries are validated against the file's
/// current stamp (length + last write time) before reuse, which keeps long-lived
/// processes (dev server) correct without file watching.
/// </summary>
internal sealed class ContentFileHashRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal void Record(string fullPath, long length, DateTime lastWriteTimeUtc, string hash)
    {
        _entries[fullPath] = new Entry(length, lastWriteTimeUtc, hash);
    }

    /// <summary>
    /// Returns the recorded hash if the file's current stamp still matches the one
    /// captured when the hash was computed; <see langword="null"/> otherwise.
    /// </summary>
    internal string? GetValidatedHash(string fullPath)
    {
        if (!_entries.TryGetValue(fullPath, out var entry))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        return info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc == entry.LastWriteTimeUtc
            ? entry.Hash
            : null;
    }

    private readonly record struct Entry(long Length, DateTime LastWriteTimeUtc, string Hash);
}
