using System.Collections.Concurrent;

namespace Kiji.Tests.TestSite;

public sealed class AssetRenderLog
{
    public ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
}
