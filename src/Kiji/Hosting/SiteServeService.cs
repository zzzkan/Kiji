using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>Owns the ASP.NET Core development server inside the site's generic host.</summary>
internal sealed class SiteServeService(IServiceProvider services, IHostApplicationLifetime lifetime,
    SiteServerArguments arguments) : BackgroundService
{
    // Hosted services are constructed before the host runs options validators. Detect
    // a replaced factory before a content-reading validator can use an unattached runtime.
    private readonly IServiceProvider _services = SiteServiceRegistration.RequireProvider(services);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            var (server, web) = await _services.GetRequiredService<SiteEngine>().StartDevServerAsync(arguments.Args, cancellation.Token);
            await using (server)
            {
                using var stopped = web.Lifetime.ApplicationStopping.Register(lifetime.StopApplication);
                await web.WaitForShutdownAsync(cancellation.Token);
            }
        }
        finally { lifetime.StopApplication(); }
    }
}
