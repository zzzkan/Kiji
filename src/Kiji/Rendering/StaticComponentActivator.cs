using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

internal sealed class StaticComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, (ObjectFactory Create, bool AlwaysRender)> Factories = new();

    [SuppressMessage("Usage", "BL0005", Justification = "Sets ImportMap's default before rendering; explicit parameters still replace it.")]
    public IComponent CreateInstance(Type componentType)
    {
        if (componentType == typeof(ImportMap))
        {
            return new ImportMap { ImportMapDefinition = (PageRenderContext.Current?.Assets ?? Assets.AssetResources.Empty).ReadImportMap() };
        }

        var (create, alwaysRender) = Factories.GetOrAdd(componentType,
            static type => (ActivatorUtilities.CreateFactory(type, Type.EmptyTypes),
                type.IsDefined(typeof(AlwaysRenderAttribute), inherit: true)
                    || typeof(ErrorBoundaryBase).IsAssignableFrom(type)));
        if (alwaysRender)
        {
            PageRenderContext.Current?.Dependencies?.DisableCache();
        }
        return (IComponent)create(services, null);
    }
}
