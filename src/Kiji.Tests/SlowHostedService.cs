namespace Kiji.Tests;

/// <summary>A user hosted service whose initialization completes asynchronously.</summary>
internal sealed class SlowHostedService : IHostedService
{
    public bool Started { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);
        Started = true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
