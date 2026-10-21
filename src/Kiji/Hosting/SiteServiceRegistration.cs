using System.Reflection;
using Kiji.Assets;
using Kiji.Generation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Kiji.Hosting;

/// <summary>Validates site-owned registrations and records code reached through DI.</summary>
internal static class SiteServiceRegistration
{
    internal static bool IsManaged(Type type) =>
        type == typeof(SiteInfo) || type == typeof(PageInfo) || type == typeof(ResolvedSitePaths)
        || type == typeof(PageBuildInputs) || type == typeof(ContentFileRegistry)
        || type == typeof(IImageProcessor) || type == typeof(NavigationManager)
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
            // Include keyed and open generic registrations rather than silently accepting alternatives.
            if (!expected[type].SequenceEqual(actual[type]))
            {
                var guidance = type == typeof(IImageProcessor) ? " Use UseImageProcessor to configure image processing." : string.Empty;
                throw new InvalidOperationException(
                    $"Service '{type.FullName}' is managed by Kiji and cannot be added, removed or replaced through UseServices.{guidance}");
            }
        }
    }

    internal static IReadOnlyCollection<Assembly> CollectAssemblies(
        IServiceCollection services, IEnumerable<Action<IServiceCollection>> callbacks)
    {
        var assemblies = new HashSet<Assembly>();
        foreach (var callback in callbacks) { AddDelegate(callback); }
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
                assemblies.Add(entry.Method.Module.Assembly);
                AddType(entry.Method.DeclaringType);
                AddType(entry.Target?.GetType());
                foreach (var argument in entry.Method.GetGenericArguments()) { AddType(argument); }
            }
        }
    }
}
