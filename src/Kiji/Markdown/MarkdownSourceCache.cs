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

    /// <param name="stamp">
    /// The file's size and last write time, already known from the directory walk that
    /// found it. Passing it in is what keeps a cache hit free of filesystem calls.
    /// </param>
    internal MarkdownSource<TFrontMatter> GetOrRead(
        string filePath,
        MarkdownFileStamp stamp,
        IDeserializer deserializer)
    {
        if (_entries.TryGetValue(filePath, out var entry) && entry.Token == stamp)
        {
            return entry.Source;
        }

        var source = MarkdownSourceReader.Read<TFrontMatter>(filePath, stamp, deserializer);
        _entries[filePath] = new Entry(new MarkdownFileStamp(source.Length, source.LastWriteTimeUtc), source);
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

    private sealed record Entry(MarkdownFileStamp Token, MarkdownSource<TFrontMatter> Source);
}
