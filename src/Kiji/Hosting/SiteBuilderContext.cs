using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>Identifies a Kiji builder and tracks the descriptors its extensions own.</summary>
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
    internal List<ServiceDescriptor> ManagedServices { get; } = [.. builder.Services.Where(static descriptor => SiteServiceRegistration.IsManaged(descriptor.ServiceType)
        || descriptor.ImplementationType == typeof(SitePublishService) || descriptor.ImplementationType == typeof(SiteServeService))];

    internal static SiteBuilderContext Get(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Properties.TryGetValue(Key, out var value) || value is not SiteBuilderContext context)
        {
            throw new InvalidOperationException("Create the builder with StaticSite.CreateBuilder before registering Kiji services.");
        }
        // HostApplicationBuilder owns the read-only transition. Check it before invoking user callbacks
        // or changing singleton options, which do not themselves mutate the service collection.
        if (builder.Services.IsReadOnly) { throw new InvalidOperationException("Configure the builder before calling Build."); }
        return context;
    }

    internal void RefreshSiteInfoRegistration()
    {
        ManagedServices.RemoveAll(static descriptor => descriptor.ServiceType == typeof(SiteInfo));
        ManagedServices.AddRange(builder.Services.Where(static descriptor => descriptor.ServiceType == typeof(SiteInfo)));
    }
}
