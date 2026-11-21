using Kiji.Incremental;
using Kiji.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji.Definition;

internal sealed class SiteBuilderContext(IHostApplicationBuilder builder, StaticSiteOptions options,
    ContentRuntime runtime, SiteExecutionPaths paths, IEnumerable<ServiceDescriptor> managedServices, ServiceDescriptor hostedService)
{
    internal static readonly object Key = new();
    internal StaticSiteOptions Options { get; } = options;
    internal ContentRuntime Runtime { get; } = runtime;
    internal SiteExecutionPaths Paths { get; } = paths;
    internal IHostEnvironment Environment { get; } = builder.Environment;
    internal string EnvironmentName { get; } = builder.Environment.EnvironmentName;
    internal string ContentRootPath { get; } = builder.Environment.ContentRootPath;
    internal List<ServiceDescriptor> ManagedServices { get; } = [.. managedServices];
    internal ServiceDescriptor HostedService { get; } = hostedService;

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
