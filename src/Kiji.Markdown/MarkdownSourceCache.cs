using System.Collections.Concurrent;
using Kiji.Incremental;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

internal sealed class MarkdownSourceCache<TFrontMatter>
{
    private readonly ConcurrentDictionary<string, MarkdownSource<TFrontMatter>> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal MarkdownSource<TFrontMatter> GetOrRead(
        FileInfo file,
        IDeserializer deserializer)
    {
        if (_entries.TryGetValue(file.FullName, out var entry) && entry.ContentHash == BuildFingerprint.HashFile(file.FullName))
        {
            return entry;
        }

        var source = MarkdownSourceReader.Read<TFrontMatter>(file, deserializer);
        _entries[file.FullName] = source;
        return source;
    }

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
