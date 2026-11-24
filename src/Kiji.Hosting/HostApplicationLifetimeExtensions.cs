using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

internal static class HostApplicationLifetimeExtensions
{
    internal static async Task WaitForStartedAsync(this IHostApplicationLifetime lifetime, CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
    }
}
