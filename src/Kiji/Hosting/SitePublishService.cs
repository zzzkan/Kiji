using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>Publishes once and stops the generic host, reporting incomplete output as failure.</summary>
internal sealed class SitePublishService(IServiceProvider services, IHostApplicationLifetime lifetime,
    IProcessExitCode exitCode, ILoggerFactory loggerFactory) : BackgroundService
{
    // Hosted services are constructed before the host runs options validators. Detect
    // a replaced factory before a content-reading validator can use an unattached runtime.
    private readonly IServiceProvider _services = SiteServiceRegistration.RequireProvider(services);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // StopApplication cancels ApplicationStopping before the host calls StopAsync.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            await _services.GetRequiredService<SiteEngine>().PublishAsync(cancellation.Token);
            exitCode.Value = 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            exitCode.Value = 1;
        }
        catch (Exception exception)
        {
            exitCode.Value = 1;
            loggerFactory.CreateLogger(BuildLoggerExtensions.CategoryName).PublishingFailed(exception);
        }
        finally { lifetime.StopApplication(); }
    }
}
