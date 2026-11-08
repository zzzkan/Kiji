using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Kiji.Hosting;

internal sealed class SiteMetricsBuilder(IServiceCollection services) : IMetricsBuilder
{
    public IServiceCollection Services { get; } = services;
}
