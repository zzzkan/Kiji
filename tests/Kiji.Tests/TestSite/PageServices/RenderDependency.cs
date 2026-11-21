namespace Kiji.Tests.TestSite.PageServices;

public sealed class RenderDependency : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
