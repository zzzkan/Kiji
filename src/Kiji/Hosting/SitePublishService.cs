using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>Publishes once and stops the generic host; RunAsync fails when the output is incomplete.</summary>
internal sealed class SitePublishService(IServiceProvider services, IHostApplicationLifetime lifetime,
    ILoggerFactory loggerFactory) : BackgroundService
{
    // Hosted services are constructed before the host runs options validators. Detect
    // a replaced factory before a content-reading validator can use an unattached runtime.
    private readonly IServiceProvider _services = SiteServiceRegistration.RequireProvider(services);
    private ExceptionDispatchInfo? _failure;
    // Only completion proves success: StopAsync can return at the shutdown timeout while
    // publishing still runs, and a host stopped early may never start ExecuteAsync.
    private volatile bool _published;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // StopApplication cancels ApplicationStopping before the host calls StopAsync.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            await lifetime.WaitForStartedAsync(cancellation.Token);
            await _services.GetRequiredService<SiteEngine>().PublishAsync(cancellation.Token);
            _published = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Reported by StopAsync as incomplete output.
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(BuildLoggerExtensions.CategoryName).PublishingFailed(exception);
            _failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally { lifetime.StopApplication(); }
    }

    // Failing ExecuteAsync would make the host log the exception twice more; failing StopAsync
    // only makes RunAsync throw, which gives the process a failure exit code.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _failure?.Throw();
        if (!_published) { throw new OperationCanceledException("Site publishing did not complete."); }
    }
}
