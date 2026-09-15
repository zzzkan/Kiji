using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Kiji.Benchmarks.MarkdownCacheCandidate;

internal sealed class YamlEventParser(ParsingEvent[] events) : IParser
{
    private int _index = -1;
    public ParsingEvent? Current => _index >= 0 && _index < events.Length ? events[_index] : null;
    public bool MoveNext() => ++_index < events.Length;
}
