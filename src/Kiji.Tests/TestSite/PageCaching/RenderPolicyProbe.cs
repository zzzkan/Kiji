using System.Collections.Concurrent;

namespace Kiji.Tests.TestSite.PageCaching;

public sealed class RenderPolicyProbe
{
    public string Value { get; set; } = "initial";
    public ConcurrentDictionary<string, int> Renders { get; } = new(StringComparer.Ordinal);

    public string Read(string reader)
    {
        Renders.AddOrUpdate(reader, 1, static (_, count) => count + 1);
        return Value;
    }
}
