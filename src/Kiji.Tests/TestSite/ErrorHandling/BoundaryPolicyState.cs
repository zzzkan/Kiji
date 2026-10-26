using System.Collections.Concurrent;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class BoundaryPolicyState
{
    public bool Fail { get; set; }

    public ConcurrentDictionary<string, int> Renders { get; } = new(StringComparer.Ordinal);
}
