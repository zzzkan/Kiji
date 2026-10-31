using System.Collections.Concurrent;

namespace Kiji.Tests;

public sealed class ContentRenderLog
{
    public ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public void Record(string page) => Counts.AddOrUpdate(page, 1, static (_, count) => count + 1);
}
