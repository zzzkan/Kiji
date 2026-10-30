using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>Exposes the site's logging registrations for the standard logging extensions.</summary>
internal sealed class SiteLoggingBuilder(IServiceCollection services) : ILoggingBuilder
{
    public IServiceCollection Services { get; } = services;
}
