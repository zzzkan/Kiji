using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

internal sealed class SitePublishService(IServiceProvider services, IHostApplicationLifetime lifetime,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly IServiceProvider _services = SiteServiceRegistration.RequireProvider(services);
    private ExceptionDispatchInfo? _failure;
    private volatile bool _published;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            await lifetime.WaitForStartedAsync(cancellation.Token);
            await _services.GetRequiredService<SiteEngine>().PublishAsync(cancellation.Token);
            _published = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(BuildLoggerExtensions.CategoryName).PublishingFailed(exception);
            _failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally { lifetime.StopApplication(); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _failure?.Throw();
        if (!_published) { throw new OperationCanceledException("Site publishing did not complete."); }
    }
}
