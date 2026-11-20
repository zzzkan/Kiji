using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

internal sealed class SiteBuilderContext(IHostApplicationBuilder builder, StaticSiteOptions options,
    ContentRuntime runtime, SiteExecutionPaths paths)
{
    internal static readonly object Key = new();
    internal StaticSiteOptions Options { get; } = options;
    internal ContentRuntime Runtime { get; } = runtime;
    internal SiteExecutionPaths Paths { get; } = paths;
    internal IHostEnvironment Environment { get; } = builder.Environment;
    internal string EnvironmentName { get; } = builder.Environment.EnvironmentName;
    internal string ContentRootPath { get; } = builder.Environment.ContentRootPath;
    internal List<ServiceDescriptor> ManagedServices { get; } = [.. builder.Services.Where(static descriptor => SiteServiceRegistration.IsManaged(descriptor.ServiceType))];
    internal ServiceDescriptor HostedService { get; } = builder.Services.Single(static descriptor => descriptor.ImplementationType == typeof(SitePublishService)
        || descriptor.ImplementationType == typeof(SiteServeService));

    internal static SiteBuilderContext Get(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Properties.TryGetValue(Key, out var value) || value is not SiteBuilderContext context)
        {
            throw new InvalidOperationException("Create the builder with StaticSite.CreateBuilder before registering Kiji services.");
        }
        if (builder.Services.IsReadOnly) { throw new InvalidOperationException("Configure the builder before calling Build."); }
        return context;
    }
}
