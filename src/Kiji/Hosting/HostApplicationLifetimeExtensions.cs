using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

internal static class HostApplicationLifetimeExtensions
{
    /// <summary>Waits until every hosted service has started; BackgroundService runs before later StartAsync calls complete.</summary>
    internal static async Task WaitForStartedAsync(this IHostApplicationLifetime lifetime, CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
    }
}
