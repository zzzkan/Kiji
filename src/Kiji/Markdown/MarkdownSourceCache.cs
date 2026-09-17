using System.Collections.Concurrent;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Reuses parsed markdown sources (front matter, body, content hash) across content
/// re-materializations as long as the source file's timestamp and length are
/// unchanged.
/// </summary>
internal sealed class MarkdownSourceCache<TFrontMatter>
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <remarks>The enumerated file carries a pre-cached size and last-write time, keeping cache hits free of filesystem calls.</remarks>
    internal MarkdownSource<TFrontMatter> GetOrRead(
        FileInfo file,
        IDeserializer deserializer)
    {
        var stamp = (file.Length, file.LastWriteTimeUtc);
        if (_entries.TryGetValue(file.FullName, out var entry) && entry.Stamp == stamp)
        {
            return entry.Source;
        }

        var source = MarkdownSourceReader.Read<TFrontMatter>(file, deserializer);
        _entries[file.FullName] = new Entry(stamp, source);
        return source;
    }

    /// <summary>
    /// Drops entries for files no longer present, keeping the cache bounded by the
    /// live content set.
    /// </summary>
    internal void Prune(IReadOnlySet<string> livePaths)
    {
        foreach (var path in _entries.Keys)
        {
            if (!livePaths.Contains(path))
            {
                _entries.TryRemove(path, out _);
            }
        }
    }

    private sealed record Entry((long Length, DateTime LastWriteTimeUtc) Stamp, MarkdownSource<TFrontMatter> Source);
}
