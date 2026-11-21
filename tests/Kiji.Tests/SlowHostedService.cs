namespace Kiji.Tests;

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
