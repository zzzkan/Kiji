using System.Collections.Concurrent;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class ServiceProbe
{
    public ConcurrentQueue<RenderService> Created { get; } = new();
    public TaskCompletionSource? Release { get; set; }
    public ConcurrentQueue<(string Place, IRenderService Service)> Reads { get; } = new();
    public ConcurrentQueue<(IRenderService Service, PageInfo Page)> PageReads { get; } = new();
}
