using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>Owns the ASP.NET Core development server inside the site's generic host.</summary>
internal sealed class SiteServeService(IServiceProvider services, IHostApplicationLifetime lifetime,
    SiteServerArguments arguments, ILoggerFactory loggerFactory) : BackgroundService
{
    // Hosted services are constructed before the host runs options validators. Detect
    // a replaced factory before a content-reading validator can use an unattached runtime.
    private readonly IServiceProvider _services = SiteServiceRegistration.RequireProvider(services);
    private ExceptionDispatchInfo? _failure;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            await lifetime.WaitForStartedAsync(cancellation.Token);
            var (server, web) = await _services.GetRequiredService<SiteEngine>().StartDevServerAsync(arguments.Args, cancellation.Token);
            await using (server)
            {
                using var stopped = web.Lifetime.ApplicationStopping.Register(lifetime.StopApplication);
                await web.WaitForShutdownAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopping the development server is a successful exit.
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(BuildLoggerExtensions.CategoryName).ServingFailed(exception);
            _failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally { lifetime.StopApplication(); }
    }

    // Failing StopAsync rather than ExecuteAsync makes RunAsync throw without duplicate host logs.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _failure?.Throw();
    }
}
