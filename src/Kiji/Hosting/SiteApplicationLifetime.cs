using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Like the generic host lifetime, tokens remain usable after site disposal. No wait handles are created.")]
internal sealed class SiteApplicationLifetime : IHostApplicationLifetime
{
    private static readonly Action<ILogger, Exception?> LogCallbackError = LoggerMessage.Define(
        LogLevel.Error, new EventId(0, "LifetimeCallbackFailed"), "An application lifetime callback failed.");

    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    internal ILogger? Logger { get; set; }

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;

    public void StopApplication() => Notify(_stopping);
    internal void NotifyStarted() => Notify(_started);
    internal void NotifyStopped() => Notify(_stopped);

    private void Notify(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (AggregateException exception)
        {
            if (Logger is { } logger) { LogCallbackError(logger, exception); }
        }
    }
}
