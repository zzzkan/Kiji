namespace Kiji.Tests.TestSite.PageServices;

public sealed class RenderService : IAsyncDisposable
{
    public RenderService(ContentDictionary<ServiceProbe> probes, RenderDependency dependency)
    {
        Probe = probes["0"];
        Dependency = dependency;
        Probe.Created.Enqueue(this);
    }

    public ServiceProbe Probe { get; }
    public RenderDependency Dependency { get; }
    public string Key { get; set; } = string.Empty;
    public int DisposeCount { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        DisposeCount++;
    }
}
