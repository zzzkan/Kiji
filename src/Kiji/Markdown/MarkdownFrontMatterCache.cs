using System.Collections.Concurrent;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Reuses parsed front matter across content re-materializations as long as the
/// source file's timestamp and length are unchanged. In the dev server, a single
/// file save re-parses one file instead of the whole content directory. Builds run
/// in a fresh process, so build correctness never depends on this cache.
/// </summary>
internal sealed class MarkdownFrontMatterCache<TFrontMatter>
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal TFrontMatter GetOrParse(string filePath, IDeserializer deserializer)
    {
        var info = new FileInfo(filePath);
        var token = (info.LastWriteTimeUtc, info.Length);

        if (_entries.TryGetValue(filePath, out var entry) && entry.Token == token)
        {
            return entry.FrontMatter;
        }

        var frontMatter = MarkdownFrontMatterParser.Parse<TFrontMatter>(filePath, deserializer);
        _entries[filePath] = new Entry(token, frontMatter);
        return frontMatter;
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

    private sealed record Entry((DateTime LastWriteTimeUtc, long Length) Token, TFrontMatter FrontMatter);
}
