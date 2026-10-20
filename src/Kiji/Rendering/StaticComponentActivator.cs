using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

/// <summary>Creates components and supplies the site's default import map.</summary>
internal sealed class StaticComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> Factories = new();

    public IComponent CreateInstance(Type componentType)
    {
        if (componentType == typeof(ImportMap))
        {
            // This is the framework component, with a default for its public parameter.
            // Explicit component parameters can still replace the default during rendering.
#pragma warning disable BL0005
            return new ImportMap { ImportMapDefinition = (PageRenderContext.Current?.Assets ?? Assets.AssetResources.Empty).ReadImportMap() };
#pragma warning restore BL0005
        }

        var factory = Factories.GetOrAdd(componentType,
            static type => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes));
        return (IComponent)factory(services, null);
    }
}
