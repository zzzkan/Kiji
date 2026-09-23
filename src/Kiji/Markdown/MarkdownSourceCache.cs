using System.Collections.Concurrent;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Reuses parsed markdown sources (front matter, body, content hash) across content
/// re-materializations when a fresh content hash proves the bytes are unchanged.
/// </summary>
internal sealed class MarkdownSourceCache<TFrontMatter>
{
    private readonly ConcurrentDictionary<string, MarkdownSource<TFrontMatter>> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <remarks>File timestamps alone never establish equivalence.</remarks>
    internal MarkdownSource<TFrontMatter> GetOrRead(
        FileInfo file,
        IDeserializer deserializer)
    {
        if (_entries.TryGetValue(file.FullName, out var entry) && entry.ContentHash == Generation.BuildFingerprint.HashFile(file.FullName))
        {
            return entry;
        }

        var source = MarkdownSourceReader.Read<TFrontMatter>(file, deserializer);
        _entries[file.FullName] = source;
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
}
