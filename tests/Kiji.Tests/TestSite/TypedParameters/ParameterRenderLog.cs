using System.Collections.Concurrent;

namespace Kiji.Tests.TestSite.TypedParameters;

public sealed class ParameterRenderLog
{
    public ConcurrentDictionary<int, int> Counts { get; } = new();
    public ConcurrentDictionary<int, TypedParameterPage> Pages { get; } = new();

    public void Record(TypedParameterPage page)
    {
        Counts.AddOrUpdate(page.Id, 1, static (_, count) => count + 1);
        Pages[page.Id] = page;
    }
}
