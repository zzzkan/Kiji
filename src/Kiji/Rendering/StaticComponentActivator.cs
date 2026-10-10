using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

/// <summary>Creates components and rejects unsupported Blazor features.</summary>
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

        if (typeof(PageTitle).IsAssignableFrom(componentType)
            || typeof(HeadContent).IsAssignableFrom(componentType)
            || typeof(HeadOutlet).IsAssignableFrom(componentType))
        {
            throw new InvalidOperationException(
                $"{componentType.FullName} is not supported by Kiji. Use Kiji.Components.StaticHeadContent "
                + "to contribute to the generated head. For a title, use <StaticHeadContent><title>...</title></StaticHeadContent>. "
                + "Kiji supplies the head outlet automatically.");
        }

        var factory = Factories.GetOrAdd(componentType,
            static type => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes));
        return (IComponent)factory(services, null);
    }
}
