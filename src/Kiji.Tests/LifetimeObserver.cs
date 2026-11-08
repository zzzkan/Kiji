
namespace Kiji.Tests;

internal sealed class LifetimeObserver(IHostApplicationLifetime lifetime) : IAsyncDisposable
{
    public IHostApplicationLifetime Lifetime { get; } = lifetime;
    public int Disposals { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }
}
