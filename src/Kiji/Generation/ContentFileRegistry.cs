using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>
/// What the content-loading pass learned about the files on disk, made available to the
/// incremental build planner: the directory listing it walked, and the hash of every
/// file it read. Both would otherwise be recomputed — the planner would walk the same
/// tree a second time and re-read files that were just parsed.
/// </summary>
/// <remarks>
/// Hashes are validated against the file's stamp (length + last write time) before
/// reuse, which keeps long-lived processes (the dev server) correct without file
/// watching. Sharing the listing also removes a real inconsistency: two walks a few
/// milliseconds apart could disagree about which files exist, and the content set's
/// fingerprint would then describe a set the content dictionary never saw.
/// </remarks>
internal sealed class ContentFileRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ScannedFile>> _scans = new(StringComparer.OrdinalIgnoreCase);

    internal void Record(string fullPath, long length, DateTime lastWriteTimeUtc, string hash)
    {
        _entries[fullPath] = new Entry(length, lastWriteTimeUtc, hash);
    }

    /// <summary>
    /// Records the markdown files found under <paramref name="directory"/>, so anything
    /// else that needs the same listing this build can have it without walking again.
    /// </summary>
    internal void RecordScan(string directory, IReadOnlyList<ScannedFile> files)
    {
        _scans[Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))] = files;
    }

    /// <summary>
    /// The listing recorded for exactly this directory, or <see langword="null"/> if
    /// nothing walked it. A parent's listing is deliberately not reused for a
    /// subdirectory: filtering it would cost as much as the walk it replaces.
    /// </summary>
    internal IReadOnlyList<ScannedFile>? GetScan(string directory)
    {
        return _scans.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), out var files)
            ? files
            : null;
    }

    /// <summary>
    /// Returns the recorded hash if the file's current stamp still matches the one
    /// captured when the hash was computed; <see langword="null"/> otherwise.
    /// </summary>
    /// <param name="stamp">
    /// The file's current size and last write time when the caller already has them
    /// from a directory walk. Omit only when there is nothing to compare against and a
    /// filesystem round trip is unavoidable.
    /// </param>
    internal string? GetValidatedHash(string fullPath, (long Length, DateTime LastWriteTimeUtc)? stamp = null)
    {
        if (!_entries.TryGetValue(fullPath, out var entry))
        {
            return null;
        }

        if (stamp is { } known)
        {
            return known.Length == entry.Length && known.LastWriteTimeUtc == entry.LastWriteTimeUtc
                ? entry.Hash
                : null;
        }

        var info = new FileInfo(fullPath);
        return info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc == entry.LastWriteTimeUtc
            ? entry.Hash
            : null;
    }

    private readonly record struct Entry(long Length, DateTime LastWriteTimeUtc, string Hash);
}
