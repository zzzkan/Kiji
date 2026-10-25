using System.Reflection;
using Kiji.Assets;
using Kiji.Generation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;

namespace Kiji.Hosting;

/// <summary>Validates site-owned registrations and records code reached through DI.</summary>
internal static class SiteServiceRegistration
{
    internal static bool IsManaged(Type type) =>
        type == typeof(SiteInfo) || type == typeof(PageInfo) || type == typeof(ResolvedSitePaths)
        || type == typeof(ContentFileRegistry) || type == typeof(IHostEnvironment) || type == typeof(IConfiguration)
        || type == typeof(NavigationManager)
        || type == typeof(IJSRuntime) || type == typeof(IComponentActivator)
        || type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory)
        || type == typeof(IKeyedServiceProvider)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ContentDictionary<>));

    internal static void Validate(IServiceCollection services, IReadOnlyList<ServiceDescriptor> managed)
    {
        var expected = managed.ToLookup(static descriptor => descriptor.ServiceType);
        var actual = services.Where(static descriptor => IsManaged(descriptor.ServiceType))
            .ToLookup(static descriptor => descriptor.ServiceType);
        foreach (var type in expected.Select(static group => group.Key).Concat(actual.Select(static group => group.Key)).Distinct())
        {
            // Descriptor identity preserves the lifetime, factory and instance owned by Kiji.
            if (!expected[type].SequenceEqual(actual[type]))
            {
                throw new InvalidOperationException(
                    $"Service '{type.FullName}' is managed by Kiji and cannot be added, removed or replaced through StaticSiteBuilder.Services.");
            }
        }

        var imageProcessor = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(IImageProcessor) && !descriptor.IsKeyedService);
        if (imageProcessor?.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                "IImageProcessor requires an unkeyed Singleton registration. Register it with AddSingleton on StaticSiteBuilder.Services.");
        }
    }

    internal static IReadOnlyCollection<Assembly> CollectAssemblies(IServiceCollection services)
    {
        var assemblies = new HashSet<Assembly>();
        foreach (var descriptor in services)
        {
            AddType(descriptor.ServiceType);
            AddType(descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType);
            AddType((descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance)?.GetType());
            AddDelegate(descriptor.IsKeyedService ? descriptor.KeyedImplementationFactory : descriptor.ImplementationFactory);
        }
        return assemblies;

        void AddType(Type? type)
        {
            if (type is null || type.IsGenericParameter) { return; }
            assemblies.Add(type.Assembly);
            if (type.HasElementType) { AddType(type.GetElementType()); }
            foreach (var argument in type.GenericTypeArguments) { AddType(argument); }
        }

        void AddDelegate(Delegate? callback)
        {
            if (callback is null) { return; }
            foreach (var entry in callback.GetInvocationList())
            {
                // A DynamicMethod's host module does not identify its generated code.
                AddType(entry.Method.DeclaringType ?? throw new NotSupportedException(
                    "Service factories must use a regular C# lambda or method, not a standalone runtime-generated method."));
                AddType(entry.Target?.GetType());
                foreach (var argument in entry.Method.GetGenericArguments()) { AddType(argument); }
            }
        }
    }
}
